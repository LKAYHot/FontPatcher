
export enum Epoch {
  Auto = 'auto',
  Legacy = 'legacy',
  Mid = 'mid',
  Modern = 'modern'
}

export enum BuildTarget {
  StandaloneWindows64 = 'StandaloneWindows64',
  StandaloneOSX = 'StandaloneOSX',
  StandaloneLinux64 = 'StandaloneLinux64',
  iOS = 'iOS',
  Android = 'Android'
}

export interface AppConfig {
  // Mode
  isBatchMode: boolean;
  
  // Single Mode
  fontPath: string;
  outputPath: string;
  bundleName: string;
  tmpName: string;

  // Batch Mode
  jobsFile: string;
  maxWorkers: number;
  continueOnJobError: boolean;

  // Unity Setup
  unityPath: string;
  unityHubPath: string;
  unityVersion: string;
  targetGame: string;
  unityInstallRoot: string;
  epoch: Epoch;
  useNographics: boolean;
  autoInstallUnity: boolean;
  autoInstallHub: boolean;
  preferNonLts: boolean;

  // Font/TMP Settings
  buildTarget: BuildTarget;
  atlasSizes: string; // CSV
  pointSize: number;
  padding: number;
  scanUpperBound: number;
  forceStatic: boolean;
  forceDynamic: boolean;
  dynamicWarmupLimit: number;
  dynamicWarmupBatch: number;
  includeControl: boolean;
  keepTemp: boolean;
}

export interface LogEntry {
  id: string;
  timestamp: string;
  level: 'info' | 'warn' | 'error' | 'success';
  message: string;
}
