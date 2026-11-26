using System.Net;

namespace ListonicApiClient.Tests;

public class LoginClassificationTests
{
    [Test]
    [Arguments(HttpStatusCode.Unauthorized, null)]
    [Arguments(HttpStatusCode.Forbidden, null)]
    [Arguments(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""")]
    [Arguments(HttpStatusCode.BadRequest, null)]
    public async Task CredentialRejections_AreBadCredentials(HttpStatusCode status, string? body) =>
        await Assert.That(ListonicLogin.Classify(status, body).Kind).IsEqualTo(LoginOutcomeKind.BadCredentials);

    [Test]
    [Arguments(HttpStatusCode.TooManyRequests)]
    [Arguments(HttpStatusCode.InternalServerError)]
    [Arguments(HttpStatusCode.BadGateway)]
    [Arguments(HttpStatusCode.ServiceUnavailable)]
    [Arguments(HttpStatusCode.NotFound)]
    public async Task OtherFailures_AreApiErrors(HttpStatusCode status) =>
        await Assert.That(ListonicLogin.Classify(status, "oops").Kind).IsEqualTo(LoginOutcomeKind.ApiError);

    [Test]
    public async Task BadRequest_WithUnrelatedBody_IsApiError() =>
        await Assert.That(ListonicLogin.Classify(HttpStatusCode.BadRequest, """{"error":"unsupported_grant_type"}""").Kind)
            .IsEqualTo(LoginOutcomeKind.ApiError);

    [Test]
    public async Task Detail_NeverContainsBody() =>
        await Assert.That(ListonicLogin.Classify(HttpStatusCode.BadRequest, "invalid_grant secret").Detail!)
            .DoesNotContain("secret");
}
