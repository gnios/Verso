/** DTOs da bridge — espelham os snapshots serializados pelo VersoBridge (C#). */

export interface AppInfo {
  version: string;
  hasUpdateChannel: boolean;
}

export interface SidebarTranscription {
  id: string;
  title: string;
  icon: string | null;
  isDone: boolean;
  statusIcon: string;
}

export interface SidebarFolder {
  id: number;
  title: string;
  icon: string;
  colorHex: string;
  transcriptionCount: number;
  isExpanded: boolean;
  transcriptions: SidebarTranscription[];
}

export interface SidebarTag {
  id: number;
  name: string;
  colorName: string;
  count: number;
}

export interface SidebarCounts {
  total: number;
  inProgress: number;
  done: number;
  error: number;
  unassigned: number;
}

export interface UpdateStatus {
  canUpdateNow: boolean;
  isReady: boolean;
  hasUpdate: boolean;
  title: string;
  label: string;
}

export interface UpdateDetails {
  version: string;
  latestVersion: string | null;
  hasChannel: boolean;
  status: string;
  action: UpdateStatus;
}

export interface SidebarData {
  folders: SidebarFolder[];
  tags: SidebarTag[];
  counts: SidebarCounts;
  versionLabel: string;
  update: UpdateStatus;
}

export type TranscriptionStatus = 'InProgress' | 'Done' | 'Error';

export interface CardTag {
  name: string;
  colorName: string;
}

// Record<string, unknown>: exigência do Table do Astryx (data-driven columns).
export interface TranscriptionCard extends Record<string, unknown> {
  id: string;
  title: string;
  icon: string;
  preview: string;
  tags: CardTag[];
  date: string;
  duration: string;
  durationSeconds: number;
  status: TranscriptionStatus;
  statusLabel: string;
  errorMessage: string | null;
  isInProgress: boolean;
  isDone: boolean;
  isError: boolean;
  canRetry: boolean;
  canDelete: boolean;
  canCancel: boolean;
  progressPercent: number | null;
  progressStage: string;
  estimatedTimeLabel: string | null;
}

export interface DashboardData {
  activeStatusFilter: string;
  isEmpty: boolean;
  cards: TranscriptionCard[];
}

export interface CardProgress {
  transcriptionId: string;
  progressPercent: number | null;
  progressStage: string;
  label: string;
}

export interface ConfirmRequest {
  title: string;
  message: string;
  danger: boolean;
}

export interface NavigationParam {
  screen?: string;
  statusFilter?: 'All' | 'Progress' | 'Done' | 'Error';
  tagId?: number;
  folderId?: number;
  transcriptionId?: string;
  unassignedOnly?: boolean;
  /** Client-side: arquivo temporário da gravação, pré-selecionado no Upload. */
  recordingPath?: string;
}

// ------------------------------------------------------------------- upload

export interface FileDescription {
  path: string | null;
  fileName: string;
  fileSize: string;
  error: string | null;
}

export interface ModelOption {
  value: string;
  label: string;
  sizeLabel: string;
  speedHint: string;
  description: string;
  whenToUse: string;
}

export interface EngineOption {
  value: string;
  label: string;
  hint: string;
}

export interface ParakeetModelOption {
  value: string;
  label: string;
  sizeLabel: string;
}

export interface UploadFolderOption {
  id: number | null;
  name: string;
  icon: string;
}

export interface UploadFormData {
  languages: { code: string; label: string }[];
  models: ModelOption[];
  engines: EngineOption[];
  parakeetModels: ParakeetModelOption[];
  icons: string[];
  folders: UploadFolderOption[];
  defaults: {
    language: string;
    quality: string;
    engine: string;
    parakeetModel: string;
    selectedFolderId: number | null;
  };
  formats: string;
}

// ------------------------------------------------------------------- editor

export interface EditorSegment {
  id: string;
  start: number;
  end: number;
  text: string;
  speakerId: string | null;
  sortOrder: number;
}

export interface EditorSpeaker {
  id: string;
  name: string;
  colorHex: string;
}

export interface EditorFolderInfo {
  id: number | null;
  title: string;
}

export interface EditorData {
  id: string;
  title: string;
  icon: string | null;
  status: TranscriptionStatus;
  isInProgress: boolean;
  isDone: boolean;
  isError: boolean;
  errorMessage: string | null;
  meta: {
    date: string;
    duration: string;
    durationSeconds: number;
    processingTime: string;
    speakerCount: number;
    model: string;
  };
  tags: CardTag[];
  folder: EditorFolderInfo;
  folders: UploadFolderOption[];
  icons: string[];
  speakers: EditorSpeaker[];
  segments: EditorSegment[];
  mediaUrl: string | null;
  hasSegments: boolean;
}

export interface SegmentSplitResult {
  before: EditorSegment;
  after: EditorSegment;
}

export interface SegmentMergeResult {
  merged: EditorSegment;
  removedId: string;
}

// ----------------------------------------------------------------- settings

export interface GpuInfo {
  name: string;
  vendor: string;
  driverVersion: string;
  kindLabel: string;
  ramLabel: string;
}

export interface SettingsData {
  profile: { name: string; email: string; institution: string };
  languages: { code: string; label: string }[];
  defaultLanguage: string;
  liveTranscriptionEnabled: boolean;
  identifySpeakersDefault: boolean;
  devices: { value: string; label: string }[];
  device: string;
  models: ModelOption[];
  defaultQuality: string;
  engines: EngineOption[];
  defaultEngine: string;
  parakeetModels: ParakeetModelOption[];
  defaultParakeetModel: string;
  recommendation: {
    quality: string | null;
    reason: string;
    detectedRamGb: number;
  };
  developer: {
    gpus: GpuInfo[];
    runtimePreference: string;
    isRuntimeLoaded: boolean;
    loadedRuntime: string;
    loadedBackend: string;
    activeGpu: string;
    activeGpuSource: string;
    logDirectory: string;
    appVersion: string;
    updateStatus: string;
  };
}

// ------------------------------------------------------------------- folder

export interface FolderData {
  id: number;
  title: string;
  description: string;
  icon: string;
  colorName: string;
  cards: TranscriptionCard[];
  isEmpty: boolean;
}

// ----------------------------------------------------------------- feedback

export interface FeedbackResult {
  statusMessage: string;
  isSuccess: boolean;
}

export interface ModelDownloadInfo {
  title: string;
  message: string;
}

export interface QueueStatusEvent {
  transcriptionId: string;
  status: string;
  errorMessage: string | null;
}

export interface QueueProgressEvent {
  transcriptionId: string;
  stage: string;
  percent: number | null;
}
