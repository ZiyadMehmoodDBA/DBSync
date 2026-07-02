import { useAuth } from '../auth/useAuth';
import { Card, CardContent, CardHeader, CardTitle } from '../../components/ui/card';
import { Badge } from '../../components/ui/badge';
import { usePreference, useSetPreference } from '../../shared/hooks/usePreferences';
import { PreferenceKeys } from '../../shared/types/preferences';

function tokenExpiryLabel(expiresAt: string): string {
  const diffMs = new Date(expiresAt).getTime() - Date.now();
  if (diffMs <= 0) return 'Expired';
  const diffMin = Math.floor(diffMs / 60_000);
  if (diffMin < 1) return 'Less than 1 minute';
  if (diffMin < 60) return `${diffMin} minutes`;
  const diffHr = Math.floor(diffMin / 60);
  return `${diffHr} hour${diffHr !== 1 ? 's' : ''}`;
}

export function ProfilePage() {
  const { user } = useAuth();

  const autoRefreshEnabled   = usePreference<boolean>(PreferenceKeys.autoRefreshEnabled,  false);
  const autoRefreshInterval  = usePreference<number> (PreferenceKeys.autoRefreshInterval, 30);
  const notificationsEnabled = usePreference<boolean>(PreferenceKeys.notificationsEnabled, true);
  const defaultLandingPage   = usePreference<string> (PreferenceKeys.defaultLandingPage,  '/dashboard');
  const { mutate: setPref }  = useSetPreference();

  if (!user) {
    return (
      <div className="p-6">
        <p className="text-neutral-500 dark:text-neutral-400">Not signed in.</p>
      </div>
    );
  }

  return (
    <div className="p-6 max-w-lg">
      <h1 className="text-2xl font-semibold mb-6">Profile</h1>
      <Card>
        <CardHeader>
          <CardTitle className="text-lg">{user.username}</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          <div>
            <p className="text-sm font-medium text-neutral-500 dark:text-neutral-400 mb-1">Roles</p>
            <div className="flex flex-wrap gap-2">
              {user.roles.length > 0
                ? user.roles.map((role) => (
                    <Badge key={role} variant="secondary">
                      {role}
                    </Badge>
                  ))
                : <span className="text-sm text-neutral-500">No roles assigned</span>}
            </div>
          </div>
          <div>
            <p className="text-sm font-medium text-neutral-500 dark:text-neutral-400 mb-1">
              Token expires in
            </p>
            <p className="text-sm">{tokenExpiryLabel(user.expiresAt)}</p>
          </div>

          <div className="mt-6 border-t pt-6">
            <h3 className="text-sm font-semibold mb-4">Application Settings</h3>

            <div className="space-y-4">
              {/* Default landing page */}
              <div className="flex items-center justify-between">
                <label className="text-sm">Default landing page</label>
                <select
                  className="text-sm border rounded px-2 py-1"
                  value={defaultLandingPage}
                  onChange={e => setPref({ key: PreferenceKeys.defaultLandingPage, value: e.target.value })}
                >
                  <option value="/dashboard">Dashboard</option>
                  <option value="/events">Events</option>
                  <option value="/incoming-batches">Incoming Batches</option>
                  <option value="/outgoing-batches">Outgoing Batches</option>
                  <option value="/audit">Audit</option>
                  <option value="/topology">Topology</option>
                  <option value="/nodes">Nodes</option>
                </select>
              </div>

              {/* Auto-refresh */}
              <div className="flex items-center justify-between">
                <label className="text-sm">Auto-refresh dashboard</label>
                <input
                  type="checkbox"
                  checked={autoRefreshEnabled}
                  onChange={e => setPref({ key: PreferenceKeys.autoRefreshEnabled, value: e.target.checked })}
                />
              </div>

              {autoRefreshEnabled && (
                <div className="flex items-center justify-between pl-4">
                  <label className="text-sm text-muted-foreground">Refresh every (seconds)</label>
                  <input
                    type="number"
                    min={10}
                    max={300}
                    className="text-sm border rounded px-2 py-1 w-20"
                    value={autoRefreshInterval}
                    onChange={e => setPref({ key: PreferenceKeys.autoRefreshInterval, value: Number(e.target.value) })}
                  />
                </div>
              )}

              {/* Toast notifications */}
              <div className="flex items-center justify-between">
                <label className="text-sm">Show event notifications</label>
                <input
                  type="checkbox"
                  checked={notificationsEnabled}
                  onChange={e => setPref({ key: PreferenceKeys.notificationsEnabled, value: e.target.checked })}
                />
              </div>
            </div>
          </div>
        </CardContent>
      </Card>
    </div>
  );
}
