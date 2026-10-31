using System.Text.Json.Serialization;
using ReapSeek.Api.Alerts;
using ReapSeek.Api.Carts;
using ReapSeek.Api.Catalog;
using ReapSeek.Api.Checkout;
using ReapSeek.Api.Customers;
using ReapSeek.Api.Health;
using ReapSeek.Api.Identity;
using ReapSeek.Api.Marketing;
using ReapSeek.Api.Media;
using ReapSeek.Api.Operations;
using ReapSeek.Api.Orders;
using ReapSeek.Api.Products;
using ReapSeek.Api.Recommendations;
using ReapSeek.Api.Returns;
using ReapSeek.Api.Reviews;
using ReapSeek.Api.Search;
using ReapSeek.Api.SellerPanel;
using ReapSeek.Api.Sellers;
using ReapSeek.Api.Stores;
using ReapSeek.Api.Wishlist;
using ReapSeek.Shared.Payouts;

namespace ReapSeek.Api.Infrastructure;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(RegisterRequest))]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(PasswordChangeRequest))]
[JsonSerializable(typeof(MeResponse))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, List<string>>))]
[JsonSerializable(typeof(CategoryRow))]
[JsonSerializable(typeof(CategoriesResponse))]
[JsonSerializable(typeof(CreateCategoryRequest))]
[JsonSerializable(typeof(BrandRow))]
[JsonSerializable(typeof(BrandsResponse))]
[JsonSerializable(typeof(CreateBrandRequest))]
[JsonSerializable(typeof(ProductsResponse))]
[JsonSerializable(typeof(ProductDetail))]
[JsonSerializable(typeof(CreateProductRequest))]
[JsonSerializable(typeof(UpdateProductRequest))]
[JsonSerializable(typeof(OfferRequest))]
[JsonSerializable(typeof(CatalogStats))]
[JsonSerializable(typeof(SellerRow))]
[JsonSerializable(typeof(SellerAccount))]
[JsonSerializable(typeof(CouponPreview))]
[JsonSerializable(typeof(ReapDeepAlertPayload))]
[JsonSerializable(typeof(AlertSubscriptionRequest))]
[JsonSerializable(typeof(AlertsResponse))]
[JsonSerializable(typeof(OutboxResponse))]
[JsonSerializable(typeof(ProductImagesResponse))]
[JsonSerializable(typeof(CustomersResponse))]
[JsonSerializable(typeof(ForgotPasswordRequest))]
[JsonSerializable(typeof(ResetPasswordRequest))]
[JsonSerializable(typeof(TokenRequest))]
[JsonSerializable(typeof(TokenCheckResponse))]
[JsonSerializable(typeof(CustomerDetail))]
[JsonSerializable(typeof(CustomerAnalytics))]
[JsonSerializable(typeof(SuspendRequest))]
[JsonSerializable(typeof(OutboxRetryRequest))]
[JsonSerializable(typeof(OutboxRetryResult))]
[JsonSerializable(typeof(CampaignRequest))]
[JsonSerializable(typeof(CampaignRow))]
[JsonSerializable(typeof(CampaignsResponse))]
[JsonSerializable(typeof(CohortsListResponse))]
[JsonSerializable(typeof(AudienceRequest))]
[JsonSerializable(typeof(AudiencePreviewResponse))]
[JsonSerializable(typeof(CustomerCouponsResponse))]
[JsonSerializable(typeof(ReviewsResponse))]
[JsonSerializable(typeof(MyReviewResponse))]
[JsonSerializable(typeof(ProductReviewRequest))]
[JsonSerializable(typeof(QuestionsResponse))]
[JsonSerializable(typeof(AskRequest))]
[JsonSerializable(typeof(AnswerRequest))]
[JsonSerializable(typeof(SellerQuestionsResponse))]
[JsonSerializable(typeof(ModerationResponse))]
[JsonSerializable(typeof(ModerationRequest))]
[JsonSerializable(typeof(CreateReturnRequest))]
[JsonSerializable(typeof(CreatedReturn))]
[JsonSerializable(typeof(RejectReturnRequest))]
[JsonSerializable(typeof(SellerReturnRow))]
[JsonSerializable(typeof(SellerReturnsResponse))]
[JsonSerializable(typeof(SellerOffersResponse))]
[JsonSerializable(typeof(SellerProductView))]
[JsonSerializable(typeof(ProposalsResponse))]
[JsonSerializable(typeof(SellerPackagesResponse))]
[JsonSerializable(typeof(SellerPackageRow))]
[JsonSerializable(typeof(SellerBalanceResponse))]
[JsonSerializable(typeof(PayoutsResponse))]
[JsonSerializable(typeof(PayoutRunResult))]
[JsonSerializable(typeof(ReviewRequest))]
[JsonSerializable(typeof(CreateSellerUserRequest))]
[JsonSerializable(typeof(SellerUserRow))]
[JsonSerializable(typeof(SellerUsersResponse))]
[JsonSerializable(typeof(SellersResponse))]
[JsonSerializable(typeof(CreateSellerRequest))]
[JsonSerializable(typeof(UpdateSellerRequest))]
[JsonSerializable(typeof(CategoryTreeResponse))]
[JsonSerializable(typeof(SearchResponse))]
[JsonSerializable(typeof(ProductCardsResponse))]
[JsonSerializable(typeof(WishlistResponse))]
[JsonSerializable(typeof(WishlistIdsResponse))]
[JsonSerializable(typeof(WishlistRequest))]
[JsonSerializable(typeof(StoreSellerResponse))]
[JsonSerializable(typeof(SuggestResponse))]
[JsonSerializable(typeof(StoreProduct))]
[JsonSerializable(typeof(RecommenderResponse))]
[JsonSerializable(typeof(RecommendationsResponse))]
[JsonSerializable(typeof(SitemapResponse))]
[JsonSerializable(typeof(AddToCartRequest))]
[JsonSerializable(typeof(UpdateCartLineRequest))]
[JsonSerializable(typeof(CartView))]
[JsonSerializable(typeof(CartSummary))]
[JsonSerializable(typeof(PlaceOrderRequest))]
[JsonSerializable(typeof(PlaceOrderResponse))]
[JsonSerializable(typeof(AddressesResponse))]
[JsonSerializable(typeof(OrdersPage))]
[JsonSerializable(typeof(OrderDetail))]
public sealed partial class AppJsonContext : JsonSerializerContext;
