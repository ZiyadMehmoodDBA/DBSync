import { useState } from 'react';
import { Button } from '../../../../components/ui/button';
import { useProvisionPackage } from '../../hooks/useProvisionPackage';
import { Eye, EyeOff, Copy, Download } from 'lucide-react';
import { toast } from 'sonner';

interface Props {
  nodeId:    string;
  token:     string | null;
  onRestart: () => void;
}

export function Step6Complete({ nodeId, token, onRestart }: Props) {
  const [revealed, setRevealed] = useState(false);
  const pkgMutation = useProvisionPackage();

  async function handleCopy() {
    if (!token) return;
    await navigator.clipboard.writeText(token);
    toast.success('Token copied to clipboard');
  }

  async function handleDownload() {
    try {
      await pkgMutation.mutateAsync({ nodeId });
      toast.success('Provision package downloaded');
    } catch {
      toast.error('Failed to download provision package');
    }
  }

  if (!token) {
    return (
      <div className="space-y-4">
        <div className="rounded-lg border border-red-300 bg-red-50 dark:bg-red-950/30 p-4">
          <p className="font-medium text-red-700 dark:text-red-300">Token cannot be recovered</p>
          <p className="text-sm text-red-600 dark:text-red-400 mt-1">
            The one-time token was only available at provisioning time. You must re-provision
            this node to generate a new token.
          </p>
        </div>
        <Button variant="outline" onClick={onRestart}>
          Return to Step 1
        </Button>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-base font-semibold">Step 6: Complete</h2>
        <p className="text-sm text-neutral-500 mt-1">
          Node <strong>{nodeId}</strong> has been provisioned.
        </p>
      </div>

      <div className="rounded-lg border border-amber-300 bg-amber-50 dark:bg-amber-950/30 p-4">
        <p className="font-medium text-amber-700 dark:text-amber-300 text-sm">
          One-time token — save it now
        </p>
        <p className="text-xs text-amber-600 dark:text-amber-400 mt-1">
          This token will not be shown again. If you navigate away before saving it,
          you must re-provision this node.
        </p>
      </div>

      <div className="space-y-2">
        <label className="text-sm font-medium">Registration Token</label>
        <div className="flex items-center gap-2">
          <div className="flex-1 rounded-md border px-3 py-2 text-sm font-mono bg-neutral-50 dark:bg-neutral-900 dark:border-neutral-700 truncate">
            {revealed ? token : '•'.repeat(Math.min(token.length, 32))}
          </div>
          <Button
            size="icon"
            variant="ghost"
            onClick={() => setRevealed(r => !r)}
            title={revealed ? 'Hide token' : 'Reveal token'}
          >
            {revealed ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
          </Button>
          <Button size="icon" variant="ghost" onClick={handleCopy} title="Copy token">
            <Copy className="h-4 w-4" />
          </Button>
        </div>
      </div>

      <Button
        variant="outline"
        onClick={handleDownload}
        disabled={pkgMutation.isPending}
        className="w-full"
      >
        <Download className="h-4 w-4 mr-2" />
        {pkgMutation.isPending ? 'Preparing…' : 'Download Provision Package'}
      </Button>

      <div className="pt-2">
        <Button variant="ghost" onClick={onRestart} className="text-sm text-neutral-500">
          Start Over
        </Button>
      </div>
    </div>
  );
}
