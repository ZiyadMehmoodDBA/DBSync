import { ShieldOff } from 'lucide-react';
import { Button } from '../../components/ui/button';
import { useNavigate } from 'react-router-dom';

export function PermissionDeniedPage() {
  const navigate = useNavigate();
  return (
    <div className="flex flex-col items-center justify-center h-full gap-4 p-6 text-center">
      <ShieldOff className="h-12 w-12 text-neutral-400" />
      <h2 className="text-xl font-semibold">Access Denied</h2>
      <p className="text-sm text-neutral-500 max-w-sm">
        You don't have permission to view this page. Contact your administrator to request access.
      </p>
      <Button variant="outline" onClick={() => navigate(-1)}>Go Back</Button>
    </div>
  );
}
