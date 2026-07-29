import type { NodeHealthScore } from '../../../shared/types/observability';

interface NodeHealthTableProps {
  scores: NodeHealthScore[];
  loading: boolean;
}

const GRADE_COLORS: Record<string, string> = {
  A: 'bg-green-100 text-green-800',
  B: 'bg-lime-100 text-lime-800',
  C: 'bg-yellow-100 text-yellow-800',
  D: 'bg-orange-100 text-orange-800',
  F: 'bg-red-100 text-red-800',
};

export function NodeHealthTable({ scores, loading }: NodeHealthTableProps) {
  if (loading) return <div className="text-muted-foreground">Loading node health scores...</div>;
  if (scores.length === 0) return <div className="text-muted-foreground">No sync nodes found.</div>;

  return (
    <div className="overflow-x-auto rounded-lg border">
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b bg-muted/50">
            <th className="p-3 text-left">Node</th>
            <th className="p-3 text-center">Grade</th>
            <th className="p-3 text-right">Score</th>
            <th className="p-3 text-right">Connectivity</th>
            <th className="p-3 text-right">Sync Lag</th>
            <th className="p-3 text-right">Error Rate</th>
            <th className="p-3 text-right">Heartbeat</th>
          </tr>
        </thead>
        <tbody>
          {scores.map((node) => (
            <tr key={node.nodeId} className="border-b hover:bg-muted/30">
              <td className="p-3 font-medium">{node.nodeName}</td>
              <td className="p-3 text-center">
                <span className={`inline-block rounded px-2 py-0.5 text-xs font-bold ${GRADE_COLORS[node.grade] ?? 'bg-gray-100 text-gray-800'}`}>
                  {node.grade}
                </span>
              </td>
              <td className="p-3 text-right font-mono">{node.score}/100</td>
              <td className="p-3 text-right font-mono">{node.connectivityScore}/40</td>
              <td className="p-3 text-right font-mono">{node.syncLagScore}/30</td>
              <td className="p-3 text-right font-mono">{node.errorRateScore}/20</td>
              <td className="p-3 text-right font-mono">{node.heartbeatScore}/10</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
