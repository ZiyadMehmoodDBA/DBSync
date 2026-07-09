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

/** Enriched parameter DTO returned by GET /api/v1/parameters?category=... */
export interface ParameterMetadataDto {
  parameterName: string;
  parameterValue: string | null;
  category: string | null;
  displayName: string | null;
  description: string | null;
  displayOrder: number | null;
  valueType: string | null;
  minimumValue: string | null;
  maximumValue: string | null;
  allowedValues: string | null;
  isSecret: boolean;
  isDynamic: boolean;
  requiresRestart: boolean;
}
