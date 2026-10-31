$ErrorActionPreference = "Stop"
Set-Location -Path $PSScriptRoot
. (Join-Path $PSScriptRoot "env.ps1")
Add-Type -AssemblyName System.Net.Http
Add-Type -AssemblyName System.Drawing

$envValues = Initialize-LocalEnv
$api = "http://127.0.0.1:8100"
$storefront = "http://127.0.0.1:3100"
$tag = "img" + [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$script:failed = 0
$script:products = @()
$work = Join-Path $env:TEMP "reapseek-images-$tag"
$null = New-Item -ItemType Directory -Force $work

function Check($desc, $cond) {
    if ($cond) { Write-Host "  OK   $desc" -ForegroundColor Green } else { Write-Host "  FAIL $desc" -ForegroundColor Red; $script:failed++ }
}

function NewClient {
    $handler = New-Object System.Net.Http.HttpClientHandler
    $handler.CookieContainer = New-Object System.Net.CookieContainer
    [pscustomobject]@{ Http = New-Object System.Net.Http.HttpClient($handler) }
}

function Result($r) {
    $text = $r.Content.ReadAsStringAsync().Result
    $parsed = $null
    if ($text -and $r.Content.Headers.ContentType -and $r.Content.Headers.ContentType.MediaType -like "*json*") { $parsed = $text | ConvertFrom-Json }
    [pscustomobject]@{ Status = [int]$r.StatusCode; Json = $parsed; Type = "$($r.Content.Headers.ContentType)"; Bytes = $r.Content.ReadAsByteArrayAsync().Result }
}

function Send($client, [string]$method, [string]$url, $body = $null) {
    $req = New-Object System.Net.Http.HttpRequestMessage((New-Object System.Net.Http.HttpMethod($method)), $url)
    if ($null -ne $body) { $req.Content = New-Object System.Net.Http.StringContent((ConvertTo-Json $body -Compress -Depth 6), [Text.Encoding]::UTF8, "application/json") }
    Result $client.Http.SendAsync($req).Result
}

function Upload($client, [string]$url, [string]$path, [string]$color = "") {
    $form = New-Object System.Net.Http.MultipartFormDataContent
    $file = New-Object System.Net.Http.ByteArrayContent(, [IO.File]::ReadAllBytes($path))
    $form.Add($file, "file", [IO.Path]::GetFileName($path))
    if ($color) { $form.Add((New-Object System.Net.Http.StringContent($color)), "color") }
    Result $client.Http.PostAsync($url, $form).Result
}

function Picture([string]$name, [int]$w, [int]$h, [string]$format) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::FromArgb(122, 75, 224))
    $g.FillEllipse([System.Drawing.Brushes]::White, [int]($w / 4), [int]($h / 4), [int]($w / 2), [int]($h / 2))
    $g.Dispose()
    $path = Join-Path $work $name
    $fmt = if ($format -eq "jpeg") { [System.Drawing.Imaging.ImageFormat]::Jpeg } else { [System.Drawing.Imaging.ImageFormat]::Png }
    $bmp.Save($path, $fmt)
    $bmp.Dispose()
    $path
}

function WaitFor([scriptblock]$cond, [int]$seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    do { if (& $cond) { return $true }; Start-Sleep -Milliseconds 700 } until ((Get-Date) -gt $deadline)
    return [bool](& $cond)
}

$admin = NewClient
if ((Send $admin POST "$api/api/v1/backoffice/auth/login" @{ email = $envValues.ADMIN_EMAIL; password = $envValues.ADMIN_PASSWORD }).Status -ne 200) { throw "Admin sign-in failed (rate limited? wait a minute)" }
function A([string]$method, [string]$path, $body = $null) { Send $admin $method "$api/api/v1/backoffice$path" $body }

function Cleanup {
    foreach ($id in $script:products) { $null = A PATCH "/catalog/products/$id" @{ status = "archived" } }
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}

trap {
    Write-Host "`nAborted: $_" -ForegroundColor Red
    Write-Host $_.InvocationInfo.PositionMessage
    Cleanup
    exit 1
}

$photo = Picture "photo.png" 800 1000 "png"
$photo2 = Picture "photo2.jpg" 640 800 "jpeg"
$tiny = Picture "tiny.png" 120 120 "png"
$text = Join-Path $work "notes.png"
[IO.File]::WriteAllText($text, "This is not an image, only a file with a .png name")
$huge = Join-Path $work "huge.png"
$bytes = New-Object byte[] (5 * 1024 * 1024 + 10)
[IO.File]::ReadAllBytes($photo)[0..23].CopyTo($bytes, 0)
[IO.File]::WriteAllBytes($huge, $bytes)

$dresses = (A GET "/catalog/categories").Json.categories | Where-Object { $_.slug -eq "dresses" }
$brand = (A GET "/catalog/brands").Json.brands | Where-Object { $_.name -eq "Velora" }

Write-Host "`n[1] Admin uploads"
$product = (A POST "/catalog/products" @{ title = "Photo Dress $tag"; brand_id = $brand.id; category_id = $dresses.id; variant_axes = @{ Color = @("Black", "Red") } }).Json
$script:products += $product.id
$base = "$api/api/v1/backoffice/catalog/products/$($product.id)/images"
$before = (Send $admin GET $base).Json.images
Check "a new product starts with generated illustrations" ($before.Count -eq 2 -and -not ($before | Where-Object { $_.uploaded }))
$up = Upload $admin $base $photo "Red"
$first = $up.Json.images[0]
Check "an upload becomes the main image" ($up.Status -eq 201 -and $first.uploaded -and $first.url -like "/media/uploads/*.png" -and $first.color -eq "Red" -and $first.alt -eq "Photo Dress $tag" -and $up.Json.images.Count -eq 3)
$served = Send (NewClient) GET "$api$($first.url)"
Check "the file is served with its type" ($served.Status -eq 200 -and $served.Type -eq "image/png" -and $served.Bytes.Length -eq (Get-Item $photo).Length)
Check "the storefront proxies it" ((Send (NewClient) GET "$storefront$($first.url)").Status -eq 200)
$up2 = Upload $admin $base $photo2
Check "a second upload goes after the first photo, before the illustrations" ($up2.Status -eq 201 -and $up2.Json.images[0].id -eq $first.id -and $up2.Json.images[1].url -like "*.jpg" -and -not $up2.Json.images[2].uploaded)
Check "positions stay 0..n-1" ((($up2.Json.images | ForEach-Object { $_.position }) -join ",") -eq "0,1,2,3")
$null = A PATCH "/catalog/products/$($product.id)" @{ status = "active" }
Check "the product detail lists the photo first" ((A GET "/catalog/products/$($product.id)").Json.images[0].url -eq $first.url)
$illustration = @($up2.Json.images | Where-Object { -not $_.uploaded })[0]
$moved = Send $admin POST "$base/$($illustration.id)/first"
Check "any image can be made the main one" ($moved.Status -eq 200 -and $moved.Json.images[0].id -eq $illustration.id -and $moved.Json.images[1].id -eq $first.id)
$null = Send $admin POST "$base/$($first.id)/first"
$slug = (A GET "/catalog/products/$($product.id)").Json.slug
Check "the storefront product page uses the photo" (WaitFor { (Send (NewClient) GET "$api/api/v1/catalog/products/$slug").Json.images[0].url -eq $first.url } 15)
Check "search cards use the photo" (WaitFor { $hit = @((Send (NewClient) GET "$api/api/v1/search?q=photo+dress+$tag").Json.products)[0]; $hit -and $hit.image_url -eq $first.url } 20)

Write-Host "`n[2] Validation"
$bad = Upload $admin $base $text
Check "a file that is not an image is rejected" ($bad.Status -eq 400 -and $bad.Json.error -like "*JPEG, PNG and WebP*")
Check "a small image is rejected" ((Upload $admin $base $tiny).Status -eq 400)
Check "a file over 5 MB is rejected" ((Upload $admin $base $huge).Status -eq 400)
Check "an unknown product is 404" ((Upload $admin "$api/api/v1/backoffice/catalog/products/999999999/images" $photo).Status -eq 404)
Check "guests cannot upload" ((Upload (NewClient) $base $photo).Status -eq 401)
Check "an unknown file name is 404" ((Send (NewClient) GET "$api/media/uploads/0123456789abcdef0123456789abcdef.png").Status -eq 404)
Check "path tricks are 404" ((Send (NewClient) GET "$api/media/uploads/..%2F..%2Fetc%2Fpasswd").Status -eq 404)
for ($i = 2; $i -lt 8; $i++) { $null = Upload $admin $base $photo2 }
$ninth = Upload $admin $base $photo2
Check "at most 8 uploads per product" ($ninth.Status -eq 409 -and @($ninth.Json).Count -ge 1)

Write-Host "`n[3] Removing"
$list = (Send $admin GET $base).Json.images
$removed = Send $admin DELETE "$base/$($first.id)"
Check "removing an upload deletes the file" ($removed.Status -eq 200 -and -not ($removed.Json.images | Where-Object { $_.id -eq $first.id }) -and (Send (NewClient) GET "$api$($first.url)").Status -eq 404)
Check "positions are renumbered" ((($removed.Json.images | ForEach-Object { $_.position }) -join ",") -eq ((0..($list.Count - 2)) -join ","))

Write-Host "`n[4] Sellers on their own proposals"
$seller = (A POST "/sellers" @{ name = "Image Seller $tag"; city = "Izmir"; commission_rate = 0.1 }).Json
$sellerEmail = "images.$tag@example.com"
$null = A POST "/sellers/$($seller.id)/users" @{ email = $sellerEmail; name = "Image Owner"; password = "seller-pass-1" }
$me = NewClient
$null = Send $me POST "$api/api/v1/backoffice/auth/login" @{ email = $sellerEmail; password = "seller-pass-1" }
$proposal = (Send $me POST "$api/api/v1/seller/catalog/products" @{ title = "Seller Photo Dress $tag"; brand_id = $brand.id; category_id = $dresses.id; variant_axes = @{ Size = @("S") } }).Json.product
$script:products += $proposal.id
$sbase = "$api/api/v1/seller/proposals/$($proposal.id)/images"
$sup = Upload $me $sbase $photo
Check "a seller adds a photo to their pending proposal" ($sup.Status -eq 201 -and $sup.Json.images[0].uploaded)
$generated = @($sup.Json.images | Where-Object { -not $_.uploaded })[0]
Check "a seller cannot remove the generated illustration" ((Send $me DELETE "$sbase/$($generated.id)").Status -eq 400)
Check "a seller cannot touch another product (404)" ((Upload $me "$api/api/v1/seller/proposals/$($product.id)/images" $photo).Status -eq 404)
Check "an admin cannot use the seller route (403)" ((Send $admin GET $sbase).Status -eq 403)
Check "the admin sees the seller's photo when reviewing" ((A GET "/catalog/products/$($proposal.id)").Json.images[0].url -eq $sup.Json.images[0].url)
$null = A POST "/catalog/products/$($proposal.id)/review" @{ decision = "approve" }
Check "after approval the seller can no longer change images" ((Upload $me $sbase $photo2).Status -eq 409 -and (Send $me GET $sbase).Status -eq 200)

Cleanup
Write-Host ""
if ($script:failed -gt 0) { Write-Host "$($script:failed) check(s) failed." -ForegroundColor Red; exit 1 }
Write-Host "All checks passed." -ForegroundColor Green
