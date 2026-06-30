import { useAuth } from '../auth/useAuth';
import { Card, CardContent, CardHeader, CardTitle } from '../../components/ui/card';
import { Badge } from '../../components/ui/badge';

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
        </CardContent>
      </Card>
    </div>
  );
}
