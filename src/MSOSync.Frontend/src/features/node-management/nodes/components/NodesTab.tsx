import { useState } from 'react';
import { NodesGrid } from './NodesGrid';

export function NodesTab() {
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null);

  return (
    <div className="flex flex-col h-full overflow-hidden">
      <NodesGrid selectedNodeId={selectedNodeId} onSelectNode={setSelectedNodeId} />
    </div>
  );
}
