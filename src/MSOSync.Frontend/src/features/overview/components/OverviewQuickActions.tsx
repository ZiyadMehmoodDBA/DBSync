import { useNavigate } from 'react-router-dom';
import { Button } from '@/components/ui/button';
import { CheckSquare, AlertOctagon, Diff, PlusCircle, Users } from 'lucide-react';

interface QuickAction {
  label: string;
  icon: React.ReactNode;
  onClick: () => void;
}

export function OverviewQuickActions() {
  const navigate = useNavigate();

  const actions: QuickAction[] = [
    {
      label: 'Approve Registrations',
      icon: <CheckSquare className="h-4 w-4" />,
      onClick: () => navigate('/node-management?tab=pending'),
    },
    {
      label: 'View Failed Jobs',
      icon: <AlertOctagon className="h-4 w-4" />,
      onClick: () => navigate('/operations/jobs?status=Failed'),
    },
    {
      label: 'Open Drift',
      icon: <Diff className="h-4 w-4" />,
      onClick: () => navigate('/operations/nodes?filter=drifted'),
    },
    {
      label: 'Create Node',
      icon: <PlusCircle className="h-4 w-4" />,
      onClick: () => navigate('/node-management?action=create'),
    },
    {
      label: 'View Workers',
      icon: <Users className="h-4 w-4" />,
      onClick: () => navigate('/operations/health'),
    },
  ];

  return (
    <div className="flex flex-wrap gap-2">
      {actions.map((a) => (
        <Button
          key={a.label}
          variant="outline"
          size="sm"
          className="gap-2"
          onClick={a.onClick}
        >
          {a.icon}
          {a.label}
        </Button>
      ))}
    </div>
  );
}
