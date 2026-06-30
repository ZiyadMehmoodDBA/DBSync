export interface TriggerDto {
  triggerId: string;
  channelId: string;
  tableName: string;
  schemaName: string;
  captureInsert: boolean;
  captureUpdate: boolean;
  captureDelete: boolean;
  enabled: boolean;
  createdTime: string;
}
