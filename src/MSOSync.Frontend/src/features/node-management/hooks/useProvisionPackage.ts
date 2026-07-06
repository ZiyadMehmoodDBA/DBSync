import { useMutation } from '@tanstack/react-query';
import { downloadProvisionPackage } from '../api/nodeManagementApi';
import type { ProvisionPackageRequest } from '../types/provision';

export function useProvisionPackage() {
  return useMutation({
    mutationFn: async (request: ProvisionPackageRequest) => {
      const blob = await downloadProvisionPackage(request);
      const url  = URL.createObjectURL(blob);
      const a    = document.createElement('a');
      a.href     = url;
      a.download = `msosync-node-${request.nodeId}.zip`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    },
  });
}
