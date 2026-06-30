export interface ParameterDto {
  name: string;
  value: string;
  isSecret: boolean;
  updatedTime?: string;
}

export interface ParameterDescriptorDto {
  name: string;
  description: string;
  isSecret: boolean;
  requiresRestart: boolean;
  isDynamic: boolean;
}
