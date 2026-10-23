import { call, on, BridgeError } from '../bridge/bridge';
import { tr } from '../i18n';
import type {
  AccountView,
  CrashEvent,
  InitData,
  InstanceSummary,
  JobInfo,
  LaunchPrompt,
  LauncherSettings,
  NotificationItem,
  SocialState,
  TextRef,
  UiState,
  UpdateView
} from '../types';

export type Page =
  | 'home'
  | 'instances'
  | 'instance'
  | 'instanceCreate'
  | 'modpacks'
  | 'localServers'
  | 'skins'
  | 'console'
  | 'settings'
  | 'welcome'
  | 'welcomeBack';

export interface Route {
  page: Page;
  id?: string;
  tab?: string;
  sub?: string;
}

export interface Toast {
  id: number;
  text: string;
  kind: 'info' | 'error' | 'success';
  action?: { label: string; run: () => void };
}

/** Der gesamte Zustand der Oberfläche an einer Stelle. */
class AppState {
  ready = $state(false);
  version = $state('');
  shortVersion = $state('');
  settings = $state<LauncherSettings | null>(null);
  ui = $state<UiState | null>(null);
  accounts = $state<AccountView[]>([]);
  instances = $state<InstanceSummary[]>([]);
  jobs = $state<Record<string, JobInfo>>({});
  unread = $state(0);
  notifications = $state<NotificationItem[]>([]);
  social = $state<SocialState>({ friends: [], inbox: [], me: null, available: false });
  update = $state<UpdateView | null>(null);
  running = $state<string[]>([]);
  route = $state<Route>({ page: 'home' });
  history: Route[] = [];
  toasts = $state<Toast[]>([]);
  prompt = $state<LaunchPrompt | null>(null);
  crash = $state<CrashEvent | null>(null);
  serverCrash = $state<{ serverId: string; serverName: string; analysis: any } | null>(null);
  devAuth = $state(false);
  curseForge = $state(false);
  totalRamMb = $state(16384);
  axoModVersions = $state<string[]>([]);
  /** Live-Konsole: pro Quelle die letzte ausgeblendete Zeile ("Logs leeren" – nur die Ansicht, der Puffer bleibt für Crash-Analyse) */
  consoleClearedAt = $state<Record<string, number>>({});
  presets = $state<Record<string, { from: string; to: string; angle: number }>>({});
  maximized = $state(false);
  /** Offener globaler Dialog (Startseite und Titelleiste). */
  dialog = $state<null | { kind: string; data?: any }>(null);

  get activeAccount(): AccountView | undefined {
    return this.accounts.find((a) => a.active);
  }

  get activeJobs(): JobInfo[] {
    return Object.values(this.jobs).filter((j) => j.state === 'running');
  }

  jobFor(kind: string, subject?: string | null): JobInfo | undefined {
    return Object.values(this.jobs).find((j) => j.kind === kind && (subject === undefined || j.subject === subject) && j.state === 'running');
  }

  async init() {
    this.subscribe();
    const data = await call<InitData>('app.init');
    this.version = data.version;
    this.shortVersion = data.shortVersion;
    this.settings = data.settings;
    this.ui = data.state;
    this.accounts = data.accounts;
    this.unread = data.unread;
    this.update = data.update;
    this.devAuth = data.devAuth;
    this.curseForge = data.curseForge;
    this.totalRamMb = data.totalRamMb;
    this.axoModVersions = data.axoModVersions;
    this.presets = data.presets;
    this.running = data.running.map((r) => r.instanceId);
    for (const job of data.jobs) this.jobs[job.id] = job;
    applyAppearance(data.settings);
    if (!data.setupDone) this.route = { page: 'welcome' };
    else if (data.needsLogin) this.route = { page: 'welcomeBack' };
    this.ready = true;
    await this.refreshInstances();
    this.refreshSocial();
    this.refreshNotifications();
  }

  private subscribe() {
    // Maximiert/wiederhergestellt auch über Doppelklick oder Aero Snap
    on<{ maximized: boolean }>('window.state', (s) => (this.maximized = s.maximized));
    on<JobInfo>('job.changed', (job) => {
      this.jobs[job.id] = job;
      if (job.state === 'failed' && job.error && job.kind !== 'launch') this.toast(tr(job.error), 'error');
      if (job.state !== 'running')
        setTimeout(() => {
          delete this.jobs[job.id];
        }, 4000);
    });
    on('instances.changed', () => this.refreshInstances());
    on('accounts.changed', (accounts: AccountView[] | null) => {
      if (Array.isArray(accounts)) this.accounts = accounts;
      else call<AccountView[]>('accounts.list').then((a) => (this.accounts = a));
    });
    on<LauncherSettings>('settings.changed', (s) => {
      this.settings = s;
      applyAppearance(s);
    });
    on<{ unread: number }>('notifications.changed', (d) => {
      this.unread = d.unread;
      this.refreshNotifications();
    });
    on<SocialState>('social.changed', (s) => (this.social = s));
    on<UpdateView>('update.available', (u) => (this.update = u));
    on<{ instanceId: string }>('game.started', (d) => {
      if (!this.running.includes(d.instanceId)) this.running = [...this.running, d.instanceId];
    });
    on<{ instanceId: string }>('game.exited', (d) => (this.running = this.running.filter((id) => id !== d.instanceId)));
    on<CrashEvent>('game.crashed', (c) => (this.crash = c));
    on('server.crashed', (c: any) => (this.serverCrash = c));
    on<LaunchPrompt>('launch.prompt', (p) => (this.prompt = p));
    on<{ promptId: string }>('launch.promptClosed', (p) => {
      if (this.prompt?.promptId === p.promptId) this.prompt = null;
    });
    on<{ text: TextRef }>('toast', (d) => this.toast(tr(d.text)));
    on<{ address: string }>('discord.join', (d) => this.joinAddress(d.address));
    // Aus dem Axo-Menü im Spiel: fehlende Mods für die offene Welt eines Freundes
    on('ingame.missingMods', (d: any) => (this.dialog = { kind: 'missingMods', data: d }));
  }

  async refreshInstances() {
    try {
      this.instances = await call<InstanceSummary[]>('instances.list');
    } catch (e) {
      this.error(e);
    }
  }

  async refreshSocial() {
    try {
      this.social = await call<SocialState>('social.state');
    } catch {
      /* Online-Service optional */
    }
  }

  async refreshNotifications() {
    try {
      const r = await call<{ items: NotificationItem[]; unread: number }>('notifications.list');
      this.notifications = r.items;
      this.unread = r.unread;
    } catch {
      /* egal */
    }
  }

  navigate(route: Route) {
    this.history.push($state.snapshot(this.route) as Route);
    if (this.history.length > 30) this.history.shift();
    this.route = route;
  }

  back() {
    const previous = this.history.pop();
    this.route = previous ?? { page: 'home' };
  }

  private toastId = 1;

  toast(text: string, kind: Toast['kind'] = 'info', action?: Toast['action']) {
    const toast: Toast = { id: this.toastId++, text, kind, action };
    this.toasts = [...this.toasts, toast].slice(-4);
    setTimeout(() => this.dismissToast(toast.id), kind === 'error' ? 7000 : 4500);
  }

  dismissToast(id: number) {
    this.toasts = this.toasts.filter((t) => t.id !== id);
  }

  /** Fehler aus der Bridge als Hinweis anzeigen. */
  error(e: unknown) {
    if (e instanceof BridgeError) {
      if (e.text.code === 'errors.cancelled') return;
      this.toast(tr(e.text), 'error');
    } else {
      console.error(e);
      this.toast(String(e), 'error');
    }
  }

  async saveSettings(change: (s: LauncherSettings) => void) {
    if (!this.settings) return;
    const copy = $state.snapshot(this.settings) as LauncherSettings;
    change(copy);
    try {
      this.settings = await call<LauncherSettings>('settings.save', { settings: copy });
      applyAppearance(this.settings);
    } catch (e) {
      this.error(e);
    }
  }

  async updateUiState(patch: Record<string, string | number>) {
    try {
      this.ui = await call<UiState>('state.update', patch);
    } catch (e) {
      this.error(e);
    }
  }

  async launch(instanceId: string, opts: { world?: string; server?: string } = {}) {
    try {
      await call('instances.launch', { id: instanceId, world: opts.world ?? null, server: opts.server ?? null });
    } catch (e) {
      this.error(e);
    }
  }

  /** Nachjoinen: in der zuletzt genutzten Instanz direkt auf den Server. */
  joinAddress(address: string) {
    const id = this.ui?.lastInstance ?? this.instances[0]?.id;
    if (!id) {
      this.toast(tr({ code: 'instances.notFound' }), 'error');
      return;
    }
    this.launch(id, { server: address });
  }
}

export const app = new AppState();

/** Akzent-Verlauf aus den Einstellungen auf die CSS-Variablen legen (gilt sofort). */
export function applyAppearance(settings: LauncherSettings | null) {
  if (!settings) return;
  const root = document.documentElement.style;
  root.setProperty('--accent-from', settings.appearance.accent.from);
  root.setProperty('--accent-to', settings.appearance.accent.to);
  root.setProperty('--accent-angle', `${settings.appearance.accent.angle}deg`);
}
