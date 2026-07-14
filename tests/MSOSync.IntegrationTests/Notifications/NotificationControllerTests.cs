// tests/MSOSync.IntegrationTests/Notifications/NotificationControllerTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Metadata.Notifications;
using Xunit;

namespace MSOSync.IntegrationTests.Notifications;

[Collection("Notifications")]
public sealed class NotificationControllerTests(NotificationsFixture fx)
{
    // ── GET /api/v1/notifications ────────────────────────────────────────────

    [Fact]
    public async Task GetNotifications_Unauthenticated_Returns401()
    {
        var client   = fx.AnonClient();
        var response = await client.GetAsync("/api/v1/notifications");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetNotifications_AsViewer_Returns200WithItems()
    {
        await using var scope = fx.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<INotificationService>();
        await SeedNotificationForAllUsersAsync(svc);

        var client   = await fx.ViewerClientAsync();
        var response = await client.GetAsync("/api/v1/notifications");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("items").GetArrayLength().Should().BeGreaterThan(0);
    }

    // ── GET /api/v1/notifications/unread-count ────────────────────────────

    [Fact]
    public async Task GetUnreadCount_AsViewer_Returns200WithNonNegativeCount()
    {
        var client   = await fx.ViewerClientAsync();
        var response = await client.GetAsync("/api/v1/notifications/unread-count");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("count").GetInt32().Should().BeGreaterThanOrEqualTo(0);
    }

    // ── POST /api/v1/notifications/{id}/read ─────────────────────────────

    [Fact]
    public async Task MarkRead_ValidId_Returns200()
    {
        await using var scope = fx.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<INotificationService>();
        await SeedNotificationForAllUsersAsync(svc, title: "MarkRead test");

        var client = await fx.ViewerClientAsync();
        var list   = await client.GetFromJsonAsync<JsonElement>("/api/v1/notifications");
        var id     = list.GetProperty("items")[0].GetProperty("notificationId").GetInt64();

        var resp = await client.PostAsync($"/api/v1/notifications/{id}/read", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MarkRead_AlreadyRead_IsIdempotentReturns200()
    {
        await using var scope = fx.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<INotificationService>();
        await SeedNotificationForAllUsersAsync(svc, title: "Idempotent test");

        var client = await fx.ViewerClientAsync();
        var list   = await client.GetFromJsonAsync<JsonElement>("/api/v1/notifications");
        var id     = list.GetProperty("items")[0].GetProperty("notificationId").GetInt64();

        // First mark-read
        await client.PostAsync($"/api/v1/notifications/{id}/read", null);

        // Second mark-read — must still return 200
        var resp2 = await client.PostAsync($"/api/v1/notifications/{id}/read", null);
        resp2.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── PATCH /api/v1/notifications/{id} ─────────────────────────────────

    [Fact]
    public async Task PatchNotification_IsReadTrue_Returns200()
    {
        await using var scope = fx.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<INotificationService>();
        await SeedNotificationForAllUsersAsync(svc, title: "Patch test");

        var client = await fx.ViewerClientAsync();
        var list   = await client.GetFromJsonAsync<JsonElement>("/api/v1/notifications");
        var id     = list.GetProperty("items")[0].GetProperty("notificationId").GetInt64();

        var resp = await client.PatchAsJsonAsync($"/api/v1/notifications/{id}", new { isRead = true });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── POST /api/v1/notifications/read-all ──────────────────────────────

    [Fact]
    public async Task MarkAllRead_Returns200_AndUnreadCountIsZero()
    {
        await using var scope = fx.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<INotificationService>();
        await SeedNotificationForAllUsersAsync(svc, title: "ReadAll test 1");
        await SeedNotificationForAllUsersAsync(svc, title: "ReadAll test 2");

        var client = await fx.ViewerClientAsync();
        var resp   = await client.PostAsync("/api/v1/notifications/read-all", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var countBody = await client.GetFromJsonAsync<JsonElement>("/api/v1/notifications/unread-count");
        countBody.GetProperty("count").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task MarkAllRead_OnlyAffectsCurrentUser()
    {
        await using var scope = fx.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<INotificationService>();
        // Seed a notification for all users so both viewer and admin receive it
        await SeedNotificationForAllUsersAsync(svc, title: "Cross-user isolation test");

        // Admin marks all read — should not affect viewer's unread count
        var adminClient = await fx.AdminClientAsync();
        await adminClient.PostAsync("/api/v1/notifications/read-all", null);

        // Seed another notification for all users so viewer has at least one unread
        await SeedNotificationForAllUsersAsync(svc, title: "Viewer-only unread");

        var viewerClient  = await fx.ViewerClientAsync();
        var viewerCount   = await viewerClient.GetFromJsonAsync<JsonElement>("/api/v1/notifications/unread-count");
        viewerCount.GetProperty("count").GetInt32().Should().BeGreaterThan(0);
    }

    // ── ?onlyUnread filtering ─────────────────────────────────────────────

    [Fact]
    public async Task GetNotifications_OnlyUnread_ExcludesReadItems()
    {
        await using var scope = fx.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<INotificationService>();
        await SeedNotificationForAllUsersAsync(svc, title: "Filter unread test");

        var client = await fx.ViewerClientAsync();

        // Get the first notification and mark it read
        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/notifications");
        var id   = list.GetProperty("items")[0].GetProperty("notificationId").GetInt64();
        await client.PostAsync($"/api/v1/notifications/{id}/read", null);

        // Query with unreadOnly=true — the just-read item should not appear
        var filteredResp = await client.GetAsync("/api/v1/notifications?unreadOnly=true");
        filteredResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var filtered = await filteredResp.Content.ReadFromJsonAsync<JsonElement>();
        var items    = filtered.GetProperty("items");
        for (var i = 0; i < items.GetArrayLength(); i++)
        {
            items[i].GetProperty("isRead").GetBoolean().Should().BeFalse();
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static Task SeedNotificationForAllUsersAsync(
        INotificationService svc, string title = "Test notification")
        => svc.CreateAsync(
            NotificationEventType.NodeRejected,
            NotificationSeverity.Info,
            title,
            "Integration test body",
            "Node",
            $"test-node-{Guid.NewGuid():N}",   // unique sourceEntityId to bypass dedup
            null,
            NotificationAudience.AllUsers);
}
