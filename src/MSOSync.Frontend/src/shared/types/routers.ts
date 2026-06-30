export interface RouterDto {
  routerId: string;
  name: string;
  sourceGroupId: string;
  targetGroupId: string;
  channelIds: string[];
  enabled: boolean;
  createdTime: string;
}
