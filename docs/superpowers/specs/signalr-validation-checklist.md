# SignalR Validation Checklist

**Status:** PENDING  
**Blocking:** Epic 12C cannot start until all items pass.

## Manual Validation Items

### M1: Basic Connection
- [ ] Open the app in a browser → SignalR status indicator shows Connected
- [ ] Server restart → indicator briefly shows Reconnecting → returns to Connected within 30s
- [ ] Network drop (disable Wi-Fi for 10s) → reconnect succeeds automatically

### M2: Token Refresh
- [ ] Let access token expire (set Jwt:AccessExpiryMinutes=1 in test env)
- [ ] Observe that refresh occurs without SignalR disconnect
- [ ] If disconnect occurs: reconnect must succeed using refreshed token

### M3: Push Events
- [ ] Trigger a node lifecycle change → UI badge updates without page refresh
- [ ] Trigger a configuration assignment → drift badge updates without page refresh
- [ ] Verify no duplicate events appear (same event shows once per tab)

### M4: Multi-Tab
- [ ] Open two browser tabs simultaneously
- [ ] Trigger a node event → both tabs update
- [ ] Close one tab → other tab continues receiving events

### M5: Long Idle Session
- [ ] Leave the app idle for 30+ minutes
- [ ] Trigger a node event → UI still receives it within 5 seconds

### M6: Browser Sleep/Resume
- [ ] Put laptop to sleep → wake → verify SignalR reconnects within 30s

### M7: Reconnect Storm
- [ ] Restart the API server 5 times in quick succession (30s intervals)
- [ ] Verify client reconnects each time without requiring manual page reload

### M8: Event Ordering
- [ ] Perform a multi-step lifecycle transition (Pending → Approved → Active)
- [ ] Verify events arrive in chronological order in the UI (CorrelationId ordering)

## Automated Tests (see SignalRResilienceTests.cs)

- [ ] `Reconnect_AfterServerBounce_EventsResume`
- [ ] `DuplicateEvent_NotDelivered_ToSameClient`

## Sign-off

Tester: ________________  Date: ________________  
All items: [ ] PASS  [ ] FAIL (list failures as blocking defects)
