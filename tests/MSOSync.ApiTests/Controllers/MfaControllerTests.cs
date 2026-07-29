using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using MSOSync.Api.Auth;
using MSOSync.Api.Controllers;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Security;
using System.Security.Claims;
using Xunit;

namespace MSOSync.ApiTests.Controllers;

/// <summary>
/// Unit tests for MfaController.
/// JwtService and MfaTokenService are concrete sealed classes (no interfaces),
/// so we instantiate them with in-memory test configuration.
/// IMfaService and IUserService are interfaces and are mocked.
/// </summary>
public sealed class MfaControllerTests : IDisposable
{
    private const string JwtSecret = "test-jwt-secret-that-is-32-chars!!";
    private const long UserId = 42L;

    private readonly Mock<IMfaService> _mfa = new();
    private readonly Mock<IUserService> _userService = new();
    private readonly MfaTokenService _mfaTokenService;
    private readonly JwtService _jwtService;
    private readonly AppDbContext _db;
    private readonly MfaController _controller;

    public MfaControllerTests()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"]              = JwtSecret,
                ["Jwt:Issuer"]              = "msosync",
                ["Jwt:Audience"]            = "msosync-dashboard",
                ["Jwt:AccessExpiryMinutes"] = "60",
            })
            .Build();

        _mfaTokenService = new MfaTokenService(cfg);
        _jwtService      = new JwtService(cfg);

        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(dbOptions);

        _db.Users.Add(new SyncUser
        {
            UserId       = UserId,
            Username     = "testuser",
            PasswordHash = "x",
            IsMfaEnabled = true,
        });
        _db.SaveChanges();

        _controller = new MfaController(
            _mfa.Object,
            _mfaTokenService,
            _jwtService,
            _userService.Object,
            _db);

        // Simulate authenticated user (userId claim = "42")
        var claims = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("userId", UserId.ToString())], "Test"));
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = claims }
        };
    }

    public void Dispose() => _db.Dispose();

    // ── Enroll ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Enroll_ReturnsOk_WithSecretAndTotpUri()
    {
        _mfa.Setup(m => m.EnrollAsync(UserId, default))
            .ReturnsAsync("JBSWY3DPEHPK3PXP");

        var result = await _controller.Enroll();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().NotBeNull();

        var body = ok.Value!.ToString()!;
        body.Should().Contain("JBSWY3DPEHPK3PXP");
        body.Should().Contain("otpauth://totp/");
    }

    // ── ConfirmEnroll ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ConfirmEnroll_Returns8BackupCodes_OnSuccess()
    {
        var codes = Enumerable.Range(0, 8).Select(i => $"code{i:D2}").ToList();
        _mfa.Setup(m => m.ConfirmEnrollmentAsync(UserId, "123456", default))
            .Returns(Task.CompletedTask);
        _mfa.Setup(m => m.GenerateBackupCodesAsync(UserId, default))
            .ReturnsAsync(codes);

        var result = await _controller.ConfirmEnroll(new ConfirmEnrollRequest("123456"));

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().NotBeNull();

        // Extract backup_codes via reflection on the anonymous object
        var backupCodes = ok.Value!.GetType().GetProperty("backup_codes")?.GetValue(ok.Value);
        backupCodes.Should().NotBeNull();
        var list = backupCodes as IReadOnlyList<string>;
        list.Should().HaveCount(8);
        list.Should().Contain("code00");
    }

    [Fact]
    public async Task ConfirmEnroll_Returns400_OnInvalidOperationException()
    {
        _mfa.Setup(m => m.ConfirmEnrollmentAsync(UserId, "000000", default))
            .ThrowsAsync(new InvalidOperationException("Invalid or expired TOTP code."));

        var result = await _controller.ConfirmEnroll(new ConfirmEnrollRequest("000000"));

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ── Verify ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Verify_ReturnsFullJwt_WhenTotpCodeValid()
    {
        var mfaToken = _mfaTokenService.Create(UserId);
        _mfa.Setup(m => m.VerifyTotpAsync(UserId, "654321", default))
            .ReturnsAsync(true);
        _userService.Setup(s => s.GetRolesAsync(UserId, default))
            .ReturnsAsync(["VIEWER"]);

        var result = await _controller.Verify(new MfaVerifyRequest(mfaToken, "654321", null));

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().NotBeNull();
        var body = ok.Value!.ToString()!;
        body.Should().Contain("token");
    }

    [Fact]
    public async Task Verify_ReturnsFullJwt_WhenBackupCodeValid()
    {
        var mfaToken = _mfaTokenService.Create(UserId);
        _mfa.Setup(m => m.VerifyBackupCodeAsync(UserId, "aabbcc", default))
            .ReturnsAsync(true);
        _userService.Setup(s => s.GetRolesAsync(UserId, default))
            .ReturnsAsync([]);

        var result = await _controller.Verify(new MfaVerifyRequest(mfaToken, null, "aabbcc"));

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Verify_Returns401_WhenMfaTokenInvalid()
    {
        var result = await _controller.Verify(new MfaVerifyRequest("bad-token", "123456", null));

        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task Verify_Returns401_WhenTotpCodeInvalid()
    {
        var mfaToken = _mfaTokenService.Create(UserId);
        _mfa.Setup(m => m.VerifyTotpAsync(UserId, "000000", default))
            .ReturnsAsync(false);

        var result = await _controller.Verify(new MfaVerifyRequest(mfaToken, "000000", null));

        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task Verify_Returns400_WhenNeitherCodeProvided()
    {
        var mfaToken = _mfaTokenService.Create(UserId);

        var result = await _controller.Verify(new MfaVerifyRequest(mfaToken, null, null));

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ── DisableMfa ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DisableMfa_Returns204_WhenTotpCodeValid()
    {
        _db.TotpSecrets.Add(new SyncUserTotpSecret
        {
            UserId    = UserId,
            Secret    = "TESTSECRET",
            IsEnabled = true,
        });
        await _db.SaveChangesAsync();

        _mfa.Setup(m => m.VerifyTotpAsync(UserId, "123456", default))
            .ReturnsAsync(true);

        var result = await _controller.DisableMfa(new ConfirmEnrollRequest("123456"));

        result.Should().BeOfType<NoContentResult>();

        var user = await _db.Users.FindAsync([UserId]);
        user!.IsMfaEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task DisableMfa_Returns400_WhenTotpCodeInvalid()
    {
        _mfa.Setup(m => m.VerifyTotpAsync(UserId, "000000", default))
            .ReturnsAsync(false);

        var result = await _controller.DisableMfa(new ConfirmEnrollRequest("000000"));

        result.Should().BeOfType<BadRequestObjectResult>();
    }
}
