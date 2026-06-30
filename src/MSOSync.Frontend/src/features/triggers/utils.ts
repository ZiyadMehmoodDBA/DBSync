export function toSourceTable(schemaName: string, tableName: string): string {
  return `${schemaName}.${tableName}`;
}

export function fromSourceTable(sourceTable: string): { schemaName: string; tableName: string } {
  const dot = sourceTable.lastIndexOf('.');
  if (dot === -1) return { schemaName: 'dbo', tableName: sourceTable };
  return { schemaName: sourceTable.slice(0, dot), tableName: sourceTable.slice(dot + 1) };
}
