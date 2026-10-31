using ReapSeek.Api.Reviews;
using ReapSeek.Shared;

namespace ReapSeek.UnitTests;

public class ReviewTests
{
    [Fact]
    public void A_review_needs_a_rating_a_title_and_a_few_words()
    {
        Assert.Equal(new ValidReview(4, "Lovely", "Fits well and the fabric is soft."), ReviewRules.Review(new ProductReviewRequest(4, " Lovely ", " Fits well and the fabric is soft. ")));
        Assert.Throws<ValidationException>(() => ReviewRules.Review(new ProductReviewRequest(0, "Lovely", "Fits well and soft")));
        Assert.Throws<ValidationException>(() => ReviewRules.Review(new ProductReviewRequest(6, "Lovely", "Fits well and soft")));
        Assert.Throws<ValidationException>(() => ReviewRules.Review(new ProductReviewRequest(5, "OK", "Fits well and soft")));
        Assert.Throws<ValidationException>(() => ReviewRules.Review(new ProductReviewRequest(5, "Lovely", "Nice")));
    }

    [Fact]
    public void Questions_and_answers_have_length_limits()
    {
        Assert.Equal("Is it true to size?", ReviewRules.Question(new AskRequest(" Is it true to size? ")));
        Assert.Throws<ValidationException>(() => ReviewRules.Question(new AskRequest("Size?")));
        Assert.Throws<ValidationException>(() => ReviewRules.Question(new AskRequest(new string('a', 501))));
        Assert.Equal("Yes", ReviewRules.Answer(new AnswerRequest("Yes")));
        Assert.Throws<ValidationException>(() => ReviewRules.Answer(new AnswerRequest(" ")));
    }

    [Theory]
    [InlineData("Ayse Yilmaz", "Ayse Y.")]
    [InlineData("Ayse Nur yilmaz", "Ayse Y.")]
    [InlineData("Cher", "Cher")]
    [InlineData("  ", "Customer")]
    public void Authors_are_shown_by_first_name_and_initial(string name, string expected) =>
        Assert.Equal(expected, ReviewRules.Author(name));

    [Fact]
    public void Distribution_lists_five_stars_first_with_zeros() =>
        Assert.Equal([2, 0, 1, 0, 3], ReviewRules.Distribution([(5, 2), (3, 1), (1, 3)]));
}
