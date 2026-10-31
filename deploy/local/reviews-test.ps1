$ErrorActionPreference = "Stop"
Set-Location -Path $PSScriptRoot
. (Join-Path $PSScriptRoot "env.ps1")
Add-Type -AssemblyName System.Net.Http

$envValues = Initialize-LocalEnv
$api = "http://127.0.0.1:8100"
$sandbox = "http://127.0.0.1:8101"
$storefront = "http://127.0.0.1:3100"
$mailpit = "http://127.0.0.1:8025"
$reapDeep = "http://127.0.0.1:8001"
$tag = "v" + [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$script:failed = 0
$script:productId = $null

trap {
    Write-Host "`nAborted: $_" -ForegroundColor Red
    Write-Host $_.InvocationInfo.PositionMessage
    if ($script:productId) { try { $null = A PATCH "/catalog/products/$($script:productId)" @{ status = "archived" } } catch {} }
    exit 1
}

function Check($desc, $cond) {
    if ($cond) { Write-Host "  OK   $desc" -ForegroundColor Green } else { Write-Host "  FAIL $desc" -ForegroundColor Red; $script:failed++ }
}

function NewClient {
    $handler = New-Object System.Net.Http.HttpClientHandler
    $handler.CookieContainer = New-Object System.Net.CookieContainer
    $handler.AllowAutoRedirect = $false
    [pscustomobject]@{ Http = New-Object System.Net.Http.HttpClient($handler); Cookies = $handler.CookieContainer }
}

function Send($client, [string]$method, [string]$url, $body = $null, [string]$form = $null, [hashtable]$headers = @{}) {
    $req = New-Object System.Net.Http.HttpRequestMessage((New-Object System.Net.Http.HttpMethod($method)), $url)
    foreach ($h in $headers.Keys) { $req.Headers.TryAddWithoutValidation($h, $headers[$h]) | Out-Null }
    if ($form) { $req.Content = New-Object System.Net.Http.StringContent($form, [Text.Encoding]::UTF8, "application/x-www-form-urlencoded") }
    elseif ($null -ne $body) {
        $json = if ($body -is [string]) { $body } else { ConvertTo-Json $body -Compress -Depth 6 }
        $req.Content = New-Object System.Net.Http.StringContent($json, [Text.Encoding]::UTF8, "application/json")
    }
    $r = $client.Http.SendAsync($req).Result
    $text = $r.Content.ReadAsStringAsync().Result
    $parsed = $null
    if ($text -and $r.Content.Headers.ContentType -and $r.Content.Headers.ContentType.MediaType -like "*json*") { $parsed = $text | ConvertFrom-Json }
    [pscustomobject]@{ Status = [int]$r.StatusCode; Json = $parsed; Text = $text }
}

function WaitFor([scriptblock]$cond, [int]$seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    do { if (& $cond) { return $true }; Start-Sleep -Milliseconds 700 } until ((Get-Date) -gt $deadline)
    return [bool](& $cond)
}

function Mails([string]$query) { (Invoke-RestMethod -UseBasicParsing "$mailpit/api/v1/search?query=$([Uri]::EscapeDataString($query))").messages_count }

function Browse($steps) {
    $file = Join-Path $env:TEMP "reapseek-reviews-$tag.json"
    [IO.File]::WriteAllText($file, (ConvertTo-Json $steps -Depth 6), (New-Object Text.UTF8Encoding($false)))
    $prev = $ErrorActionPreference; $ErrorActionPreference = "Continue"
    $out = & node (Join-Path $PSScriptRoot "tools\browse.mjs") $file 2>&1 | Out-String
    $ErrorActionPreference = $prev
    Remove-Item $file -Force -ErrorAction SilentlyContinue
    $out | ConvertFrom-Json
}

$admin = NewClient
if ((Send $admin POST "$api/api/v1/backoffice/auth/login" @{ email = $envValues.ADMIN_EMAIL; password = $envValues.ADMIN_PASSWORD }).Status -ne 200) { throw "Admin sign-in failed (rate limited? wait a minute)" }
function A([string]$method, [string]$path, $body = $null) { Send $admin $method "$api/api/v1/backoffice$path" $body }

Write-Host "`n[1] Two customers receive the product"
$seller = (A POST "/sellers" @{ name = "Review Seller $tag"; city = "Izmir" }).Json
$null = A POST "/sellers/$($seller.id)/users" @{ email = "vseller.$tag@example.com"; name = "Review Desk"; password = "seller-pass-1" }
$me = NewClient
$null = Send $me POST "$api/api/v1/backoffice/auth/login" @{ email = "vseller.$tag@example.com"; password = "seller-pass-1" }
function S([string]$method, [string]$path, $body = $null) { Send $me $method "$api/api/v1/seller$path" $body }
$other = (A POST "/sellers" @{ name = "Other Seller $tag"; city = "Izmir" }).Json
$null = A POST "/sellers/$($other.id)/users" @{ email = "oseller.$tag@example.com"; name = "Other Desk"; password = "seller-pass-1" }
$them = NewClient
$null = Send $them POST "$api/api/v1/backoffice/auth/login" @{ email = "oseller.$tag@example.com"; password = "seller-pass-1" }

$dresses = (A GET "/catalog/categories").Json.categories | Where-Object { $_.slug -eq "dresses" }
$brand = (A GET "/catalog/brands").Json.brands | Where-Object { $_.name -eq "Velora" }
$product = (A POST "/catalog/products" @{ title = "Review Dress $tag"; brand_id = $brand.id; category_id = $dresses.id; variant_axes = @{ Size = @("M") } }).Json
$script:productId = $product.id
$id = $product.id
$null = A PUT "/catalog/offers" @{ seller_id = $seller.id; variant_id = $product.variants[0].id; price = 150; stock = 10 }
$null = A PATCH "/catalog/products/$id" @{ status = "active" }
$offerId = ((A GET "/catalog/products/$id").Json.variants[0].offers | Where-Object { $_.seller_id -eq $seller.id }).id
$address = @{ full_name = "Review Buyer"; phone = "0100 000 00 01"; city = "Izmir"; district = "Konak"; line = "Ataturk Cd. 1"; postal_code = "35210" }

function Customer([string]$name, [string]$display) {
    $c = NewClient
    $null = Send $c POST "$api/api/v1/auth/register" @{ email = "$name.$tag@example.com"; name = $display; password = "buyer-pass-1" }
    $c
}
function BuyAndShip($c) {
    $null = Send $c POST "$api/api/v1/cart/items" @{ offer_id = $offerId; quantity = 1 }
    $cart = (Send $c GET "$api/api/v1/cart").Json
    $placed = (Send $c POST "$api/api/v1/checkout" @{ address = $address; expected_total = $cart.total }).Json
    $payId = $placed.payment_url.Substring($placed.payment_url.LastIndexOf("/") + 1)
    $pay = Send (NewClient) POST "$sandbox/pay/$payId" $null "holder=Review+Buyer&card_number=4242424242424242&expiry=12%2F30&cvc=123"
    if ($pay.Status -eq 302) { $null = Send (NewClient) POST "$sandbox/pay/$payId/3ds" $null "code=123456&action=confirm" }
    $null = WaitFor { (Send $c GET "$api/api/v1/orders/$($placed.order_number)").Json.status -eq "paid" } 20
    $package = (Send $c GET "$api/api/v1/orders/$($placed.order_number)").Json.packages[0]
    $null = S POST "/packages/$($package.id)/ship"
    $placed.order_number
}
$ayse = Customer "ayse" "Ayse Yilmaz"
$mert = Customer "mert" "Mert Demir"
$nobody = Customer "nobody" "Nobody Buyer"
$order1 = BuyAndShip $ayse
$order2 = BuyAndShip $mert
function Mine($c) { (Send $c GET "$api/api/v1/catalog/products/$id/my-review").Json }
Check "a customer whose order is on the way cannot review yet" (-not (Mine $ayse).eligible)
Check "both packages are delivered by the carrier" (WaitFor { (Send $ayse GET "$api/api/v1/orders/$order1").Json.packages[0].status -eq "delivered" -and (Send $mert GET "$api/api/v1/orders/$order2").Json.packages[0].status -eq "delivered" } 60)

Write-Host "`n[2] Reviews"
Check "a buyer who received it may review" ((Mine $ayse).eligible -and $null -eq (Mine $ayse).review)
Check "someone who did not buy it may not (403)" (-not (Mine $nobody).eligible -and (Send $nobody PUT "$api/api/v1/catalog/products/$id/review" @{ rating = 5; title = "Great"; body = "Never bought it but great" }).Status -eq 403)
Check "guests cannot review (401)" ((Send (NewClient) PUT "$api/api/v1/catalog/products/$id/review" @{ rating = 5; title = "Great"; body = "Lovely dress here" }).Status -eq 401)
Check "a rating must be 1 to 5" ((Send $ayse PUT "$api/api/v1/catalog/products/$id/review" @{ rating = 6; title = "Great"; body = "Lovely dress here" }).Status -eq 400)
$first = Send $ayse PUT "$api/api/v1/catalog/products/$id/review" @{ rating = 4; title = "Lovely fabric"; body = "Soft and fits true to size." }
Check "the buyer posts a review" ($first.Status -eq 200 -and $first.Json.review.rating -eq 4 -and $first.Json.review.verified -and $first.Json.review.variant_label -eq "Size: M")
$null = Send $ayse PUT "$api/api/v1/catalog/products/$id/review" @{ rating = 5; title = "Lovely fabric"; body = "Soft, fits true to size and washes well." }
$null = Send $mert PUT "$api/api/v1/catalog/products/$id/review" @{ rating = 2; title = "Too thin"; body = "The fabric is thinner than I expected." }
$reviews = (Send (NewClient) GET "$api/api/v1/catalog/products/$id/reviews").Json
Check "editing keeps one review per customer: average 3.5 from 2" ($reviews.summary.count -eq 2 -and $reviews.summary.average -eq 3.5 -and (($reviews.summary.distribution) -join ",") -eq "1,0,0,1,0")
Check "authors are shown as first name and initial" (@($reviews.reviews | ForEach-Object { $_.author }) -contains "Ayse Y." -and @($reviews.reviews | ForEach-Object { $_.author }) -contains "Mert D.")
Check "reviews can be sorted lowest first" ((Send (NewClient) GET "$api/api/v1/catalog/products/$id/reviews?sort=lowest").Json.reviews[0].rating -eq 2)
Check "search shows the rating on the card" (WaitFor { $c = (Send (NewClient) GET "$api/api/v1/search?q=review+dress+$tag").Json.products[0]; $c.rating -eq 3.5 -and $c.review_count -eq 2 } 20)
Check "search can sort by rating" ((Send (NewClient) GET "$api/api/v1/search?q=dress&sort=rating").Status -eq 200)
$page = (Send (NewClient) GET "$storefront/p/$($product.slug)").Text
Check "the product page renders the rating and the reviews for search engines" ($page -cmatch 'data-testid="reviews-average"><strong>3.5' -and $page -cmatch '"aggregateRating":\{"@type":"AggregateRating","ratingValue":3.5,"reviewCount":2' -and $page -cmatch 'Too thin')

Write-Host "`n[3] Questions and answers"
Check "a question needs a few words" ((Send $nobody POST "$api/api/v1/catalog/products/$id/questions" @{ body = "Size?" }).Status -eq 400)
Check "guests cannot ask (401)" ((Send (NewClient) POST "$api/api/v1/catalog/products/$id/questions" @{ body = "Is it lined inside?" }).Status -eq 401)
$asked = Send $nobody POST "$api/api/v1/catalog/products/$id/questions" @{ body = "Is the dress lined inside?" }
Check "any signed-in shopper can ask" ($asked.Status -eq 201 -and $asked.Json.questions[0].body -eq "Is the dress lined inside?" -and $asked.Json.questions[0].author -eq "Nobody B.")
$qid = $asked.Json.questions[0].id
Check "the selling seller gets an email" (WaitFor { (Mails "subject:""New question about Review Dress $tag""") -ge 1 } 20)
Check "it waits in the seller center" (@((S GET "/questions?answered=false").Json.questions | Where-Object { $_.id -eq $qid }).Count -eq 1)
Check "a seller who does not sell it cannot answer (404)" ((Send $them PUT "$api/api/v1/seller/questions/$qid/answer" @{ body = "Yes" }).Status -eq 404)
$answered = S PUT "/questions/$qid/answer" @{ body = "Yes, it has a cotton lining." }
Check "the seller answers" ($answered.Status -eq 200 -and $answered.Json.questions[0].my_answer -eq "Yes, it has a cotton lining.")
$public = (Send (NewClient) GET "$api/api/v1/catalog/products/$id/questions").Json.questions | Where-Object { $_.id -eq $qid }
Check "the answer is public under the store name" ($public.answers[0].seller_name -eq "Review Seller $tag" -and $public.answers[0].body -eq "Yes, it has a cotton lining.")
Check "the shopper is emailed the answer" (WaitFor { (Mails "subject:""Review Seller $tag answered your question""") -eq 1 } 20)
Check "an answered question moves out of the to-do list" (@((S GET "/questions?answered=false").Json.questions | Where-Object { $_.id -eq $qid }).Count -eq 0)

Write-Host "`n[4] Moderation"
$mertReview = ((Send (NewClient) GET "$api/api/v1/catalog/products/$id/reviews").Json.reviews | Where-Object { $_.author -eq "Mert D." }).id
Check "sellers cannot moderate (403)" ((Send $me PATCH "$api/api/v1/backoffice/reviews/$mertReview" @{ status = "hidden" }).Status -eq 403)
Check "the admin hides a review" ((A PATCH "/reviews/$mertReview" @{ status = "hidden" }).Status -eq 204)
Check "the rating no longer counts it" ((Send (NewClient) GET "$api/api/v1/catalog/products/$id/reviews").Json.summary.count -eq 1 -and (WaitFor { (Send (NewClient) GET "$api/api/v1/search?q=review+dress+$tag").Json.products[0].rating -eq 5 } 20))
Check "the admin hides a question" ((A PATCH "/questions/$qid" @{ status = "hidden" }).Status -eq 204 -and (Send (NewClient) GET "$api/api/v1/catalog/products/$id/questions").Json.total -eq 0)
Check "the moderation list shows hidden items" (@((A GET "/moderation?status=hidden").Json.reviews | Where-Object { $_.id -eq $mertReview }).Count -eq 1)

Write-Host "`n[5] In a real browser"
$session = ($ayse.Cookies.GetCookies([Uri]$api) | Where-Object { $_.Name -ceq "rs_session" }).Value
$run = Browse @(
    @{ cookie = @{ name = "rs_session"; value = $session } },
    @{ goto = "http://localhost:3100/p/$($product.slug)" },
    @{ waitFor = "document.querySelector('[data-testid=write-review]')" },
    @{ name = "button"; eval = "document.querySelector('[data-testid=write-review]').textContent" },
    @{ click = "[data-testid=write-review]" },
    @{ waitFor = "document.querySelector('[data-testid=review-form] [data-star=""3""]')" },
    @{ click = "[data-testid=review-form] [data-star=""3""]" },
    @{ click = "[data-testid=review-form] button[type=submit]" },
    @{ waitFor = "!document.querySelector('[data-testid=review-form]')" },
    @{ eval = "(() => { const i = document.querySelector('[data-testid=ask-form] input'); Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(i, 'Does it come in other colors?'); i.dispatchEvent(new Event('input', { bubbles: true })); return true })()"; name = "typed" },
    @{ sleep = 300 },
    @{ click = "[data-testid=ask-form] button[type=submit]" },
    @{ waitFor = "[...document.querySelectorAll('[data-question] .question')].some(q => q.textContent.includes('other colors'))" },
    @{ sleep = 1500 },
    @{ goto = "about:blank" },
    @{ sleep = 1500 }
)
Check "the browser run finished without errors ($($run.failure))" ($null -eq $run.failure -and @($run.errors).Count -eq 0)
Check "the buyer edits their review from the product page" ($run.results.button -eq "Edit your review" -and (Mine $ayse).review.rating -eq 3)
Check "and asks a question from the product page" (@((Send (NewClient) GET "$api/api/v1/catalog/products/$id/questions").Json.questions | Where-Object { $_.body -eq "Does it come in other colors?" }).Count -eq 1)
if ($envValues.REAPDEEP_READ_KEY) {
    $customerId = (Send $ayse GET "$api/api/v1/auth/me").Json.user.id
    function Events([string]$event) { @((Send (NewClient) GET "$reapDeep/api/v1/events?distinct_id=customer-$customerId&event=$event&limit=10" $null $null @{ Authorization = "Bearer $($envValues.REAPDEEP_READ_KEY)" }).Json.events) }
    Check "review_submitted and question_asked reached ReapDeep" (WaitFor { @(Events "review_submitted" | Where-Object { $_.properties.rating -eq 3 -and $_.columns.item_id -eq "$id" }).Count -ge 1 -and @(Events "question_asked").Count -ge 1 } 60)
}

$null = A PATCH "/catalog/products/$id" @{ status = "archived" }

Write-Host ""
if ($script:failed -eq 0) { Write-Host "All checks passed." -ForegroundColor Green } else { Write-Host "$($script:failed) check(s) failed." -ForegroundColor Red; exit 1 }
