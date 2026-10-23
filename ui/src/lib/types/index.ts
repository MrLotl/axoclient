/**
 * Spiegel der C#-Modelle (camelCase wie in Json.Options). Sollen später automatisch aus den C#-Klassen
 * erzeugt werden.
 */

export interface TextRef {
  code: string;
  params?: Record<string, unknown> | null;
}

export type LoaderType = 'vanilla' | 'fabric' | 'forge' | 'neoforge' | 'quilt';
export type ModLockLevel = 'off' | 'pack' | 'full';
export type VersionType = 'release' | 'snapshot' | 'old';
export type ContentType = 'mod' | 'resourcePack' | 'shaderPack';
export type ContentSource = 'modrinth' | 'curseforge' | 'local' | 'friend' | 'axoclient';
export type ProjectKind = 'mod' | 'resourcePack' | 'shaderPack' | 'modpack';
export type SearchSort = 'popular' | 'relevance' | 'newest' | 'updated';

export interface AccentGradient {
  from: string;
  to: string;
  angle: number;
}

export interface CustomGradient extends AccentGradient {
  id: string;
}

export interface LauncherSettings {
  formatVersion: number;
  autostart: boolean;
  afterLaunch: 'open' | 'minimize' | 'hide' | 'close';
  game: { windowMode: 'window' | 'maximized' | 'fullscreen'; windowSize: { w: number; h: number }; checkModsOnLaunch: boolean };
  defaults: { memoryMb: number; java: string };
  integrations: { discordRpc: boolean; discordShow: 'always' | 'playing'; friendJoinRelay: boolean; tablistIcon: boolean; streamerMode: boolean };
  social: { showOnline: boolean; allowJoin: boolean };
  appearance: { preset: string; accent: AccentGradient; customGradients: CustomGradient[]; waveOnStart: boolean };
  notifications: {
    windowsToasts: boolean;
    quietWhilePlaying: boolean;
    invitesWhilePlaying: boolean;
    friendOnServerScope: 'active' | 'all';
    types: Record<string, boolean>;
  };
  updates: { auto: boolean; channel: 'stable' | 'beta' };
  setupDone: boolean;
}

export interface UiState {
  lastInstance: string | null;
  views: {
    instances: 'grid' | 'list';
    localServers: 'grid' | 'list';
    modpacks: 'grid' | 'list';
    content: 'grid' | 'list';
    contentGridSize: number;
    contentListSize: number;
  };
  sort: { modpacks: SearchSort; addMods: SearchSort };
  contentSource: { addMods: string; modpacks: string };
}

export interface AccountView {
  uuid: string;
  name: string;
  type: 'microsoft' | 'dev';
  active: boolean;
  headUrl: string;
  needsLogin: boolean;
}

export interface InstanceSummary {
  id: string;
  name: string;
  minecraftVersion: string;
  loader: LoaderType;
  loaderVersion: string | null;
  iconUrl: string;
  bannerUrl: string | null;
  hasIcon: boolean;
  lastPlayed: string | null;
  totalSeconds: number;
  running: boolean;
  modCount: number;
  createdAt: string;
  modLock: ModLockLevel;
}

export interface Instance {
  id: string;
  name: string;
  minecraft: { version: string; type: VersionType };
  loader: { type: LoaderType; version: string | null };
  memoryMb: number | null;
  javaArgs: string;
  images: { icon: string | null; banner: string | null; bannerAuto: boolean };
  origin: { type: 'empty' | 'copy' | 'import' | 'modpack'; source?: string; projectId?: string; versionId?: string; versionName?: string };
  createdAt: string;
  axoMod: boolean;
  modLock: { level: ModLockLevel; packMods: string[] };
}

export interface InstanceDetails {
  instance: Instance;
  summary: InstanceSummary;
  worlds: number;
  axoModSupported: boolean;
  effectiveMemoryMb: number;
}

export interface ActivityLog {
  totalSeconds: number;
  firstPlayed: string | null;
  lastPlayed: string | null;
  lastSessionSeconds: number;
  days: Record<string, number>;
}

export interface ContentItem {
  file: string;
  name: string;
  version: string | null;
  enabled: boolean;
  source: ContentSource;
  projectId: string | null;
  versionId: string | null;
  iconUrl: string | null;
  author: string | null;
  size: number;
  addedAt: string;
  locked: boolean;
  modLocked: boolean;
}

export interface ContentProject {
  source: string;
  id: string;
  slug: string;
  title: string;
  author: string;
  description: string;
  iconUrl: string | null;
  imageUrl: string | null;
  downloads: number;
  categories: string[];
  loaders: string[];
  gameVersions: string[];
  updated: string | null;
  webUrl: string;
}

export interface SearchPage {
  items: ContentProject[];
  total: number;
}

export interface ContentFile {
  url: string;
  fileName: string;
  sha1: string | null;
  sha512: string | null;
  size: number;
  primary: boolean;
}

export interface ContentVersion {
  source: string;
  id: string;
  projectId: string;
  name: string;
  versionNumber: string;
  channel: string;
  published: string;
  gameVersions: string[];
  loaders: string[];
  files: ContentFile[];
  dependencies: { projectId: string | null; versionId: string | null; kind: string }[];
  changelog: string | null;
}

export interface ContentUpdate {
  type: ContentType;
  file: string;
  name: string;
  currentVersion: string | null;
  newVersion: string;
  newVersionId: string;
  source: string;
  projectId: string;
  iconUrl: string | null;
}

export interface WorldInfo {
  folder: string;
  name: string;
  gameMode: string;
  hardcore: boolean;
  cheats: boolean;
  version: string | null;
  lastPlayed: string | null;
  sizeBytes: number;
  iconUrl: string | null;
}

export interface BackupInfo {
  file: string;
  world: string;
  createdAt: string;
  sizeBytes: number;
}

export interface ServerEntry {
  index: number;
  name: string;
  address: string;
  iconUrl: string | null;
  acceptTextures: boolean;
}

export interface ServerStatus {
  online: boolean;
  players: number;
  maxPlayers: number;
  motd: string;
  latencyMs: number | null;
  favicon: string | null;
  version: string | null;
}

export interface ScreenshotInfo {
  file: string;
  takenAt: string;
  sizeBytes: number;
  url: string;
  width: number | null;
  height: number | null;
}

export type JobState = 'running' | 'succeeded' | 'failed' | 'cancelled';

export interface JobInfo {
  id: string;
  kind: string;
  title: TextRef;
  state: JobState;
  progress: number | null;
  status: TextRef | null;
  error: TextRef | null;
  subject: string | null;
}

export type Severity = 'ok' | 'autoFix' | 'warning' | 'blocker';

export interface IssueView {
  id: string;
  severity: Severity;
  message: TextRef;
  detail: TextRef | null;
  actions: { id: string; label: TextRef; primary: boolean }[];
  canSilence: boolean;
}

export interface LaunchPrompt {
  promptId: string;
  instanceId: string;
  phase: string;
  issues: IssueView[];
}

export interface CrashAnalysis {
  knownCause: boolean;
  cause: TextRef;
  ruleId: string | null;
  actions: string[];
  suspects: { file: string; name: string }[];
  recentlyAdded: { file: string; name: string }[];
  configFile: string | null;
  crashReportFile: string | null;
  exitCode: number;
}

export interface CrashEvent {
  instanceId: string;
  instanceName: string;
  analysis: CrashAnalysis;
}

export interface LogLine {
  seq: number;
  time: string;
  level: 'info' | 'warn' | 'error' | 'debug';
  thread: string;
  text: string;
  stdErr: boolean;
}

export type ServerSoftware = 'vanilla' | 'paper' | 'fabric' | 'velocity';

export interface ServerView {
  id: string;
  name: string;
  software: ServerSoftware;
  version: string;
  port: number;
  maxPlayers: number;
  memoryMb: number;
  openToInternet: boolean;
  eulaAccepted: boolean;
  running: boolean;
  onlinePlayers: number;
  players: string[];
  memoryUsedMb: number | null;
  startedAt: string | null;
  publicAddress: string | null;
  internetProblem: TextRef | null;
  iconUrl: string;
  bannerUrl: string | null;
  modCount: number | null;
  totalUptimeSeconds: number;
}

export type Relation = 'friends' | 'pending' | 'incoming';
export type PresenceState = 'offline' | 'launcher' | 'ingame';

export interface Friend {
  uuid: string;
  name: string;
  relation: Relation;
  state: PresenceState;
  since: number | null;
  join: string | null;
}

export interface InboxItem {
  id: string;
  sender: { uuid: string; name: string };
  kind: 'instance' | 'mod' | 'server' | 'invite';
  title: string;
  created: number;
  expires: number;
}

/** Eintrag der eigenen Blockliste; created in Unix-Millisekunden. */
export interface BlockedPlayer {
  uuid: string;
  name: string;
  created: number;
}

export interface SocialState {
  friends: Friend[];
  inbox: InboxItem[];
  me: { uuid: string; name: string; role: string } | null;
  available: boolean;
}

export interface NotificationItem {
  id: string;
  type: string;
  text: TextRef;
  created: string;
  read: boolean;
  data: Record<string, unknown> | null;
  fromService: boolean;
}

export type SkinModel = 'classic' | 'slim';

export interface SkinView {
  id: string;
  name: string;
  model: SkinModel;
  origin: 'upload' | 'player' | 'mojang';
  player: string | null;
  url: string;
  active: boolean;
}

export interface MojangProfile {
  uuid: string;
  name: string;
  skinUrl: string | null;
  model: SkinModel;
  capes: { id: string; alias: string; active: boolean; url: string | null }[];
}

export interface CapeView {
  id: string;
  name: string;
  resolution: string;
  source: 'editor' | 'upload';
  status: 'local' | 'pending' | 'approved' | 'rejected';
  reason: string | null;
  url: string;
  active: boolean;
  hasEditState: boolean;
}

export interface OfficialCape {
  id: string;
  name: string;
  url: string;
}

export interface UpdateView {
  version: string;
  sizeBytes: number;
  notes: string | null;
  downloaded: boolean;
}

export interface DiagnosticItem {
  area: string;
  status: 'ok' | 'warning' | 'error';
  title: TextRef;
  detail: TextRef | null;
  solution: TextRef | null;
}

export interface StorageOverview {
  instances: { id: string; name: string; worlds: number; mods: number; rest: number }[];
  backups: number;
  game: number;
  java: number;
  cache: number;
  logs: number;
  servers: number;
  total: number;
}

export interface FileRef {
  id: string;
  name: string;
  size?: number;
}

export interface InitData {
  version: string;
  shortVersion: string;
  settings: LauncherSettings;
  state: UiState;
  accounts: AccountView[];
  needsLogin: boolean;
  setupDone: boolean;
  devAuth: boolean;
  curseForge: boolean;
  unread: number;
  update: UpdateView | null;
  jobs: JobInfo[];
  running: { instanceId: string; name: string; startedAt: string; pid: number }[];
  serviceAvailable: boolean;
  totalRamMb: number;
  axoModVersions: string[];
  presets: Record<string, AccentGradient>;
}

// ---- Import aus anderen Launchern ----

export interface ForeignInstanceView {
  id: string;
  name: string;
  loader: LoaderType | null;
  minecraftVersion: string | null;
  loaderVersion: string | null;
  worlds: number;
  mods: number;
  shared: boolean;
  modrinthMods: number;
  hint: TextRef | null;
  problem: TextRef | null;
}

export interface ForeignLauncherView {
  id: string;
  name: string;
  installed: boolean;
  instances: ForeignInstanceView[];
  problem: TextRef | null;
}

export interface ForeignImportParts {
  worlds: boolean;
  mods: boolean;
  packs: boolean;
  settings: boolean;
  screenshots: boolean;
}

export interface ForeignImportResult {
  name: string;
  instanceId: string | null;
  notes: TextRef[];
  error: TextRef | null;
}

export interface ServerImportDraft {
  folderRef: string;
  info: {
    name: string;
    software: ServerSoftware;
    version: string | null;
    port: number;
    maxPlayers: number;
    eulaAccepted: boolean;
    worlds: number;
    plugins: number;
    mods: number;
  };
}
