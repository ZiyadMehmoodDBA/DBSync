export type PluginStatus = 'Discovered' | 'Validated' | 'Loaded' | 'Disabled' | 'Failed';

export interface PluginDto {
  pluginId:          string;
  name:              string;
  version:           string;
  status:            PluginStatus;
  loadDurationMs:    number;
  loadedAt:          string;
  lastError:         string | null;
  failureStage:      string | null;
  hostCompatibility: string;
  capabilities:      string[];
  permissions:       string[];
  dependencies:      string[];
}

export interface PluginSummaryDto {
  total:             number;
  loaded:            number;
  failed:            number;
  disabled:          number;
  startupDurationMs: number;
  lastScanAt:        string | null;
}

export interface PluginManifestDto {
  id:             string;
  name:           string;
  version:        string;
  minHostVersion: string;
  maxHostVersion: string;
  entryAssembly:  string;
  entryType:      string;
  author:         string;
  description:    string;
  permissions:    string[];
  dependencies:   string[];
  capabilities:   string[];
}
