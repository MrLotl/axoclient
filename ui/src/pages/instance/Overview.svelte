<script lang="ts">
  import { onMount } from 'svelte';
  import { call } from '../../lib/bridge/bridge';
  import { t } from '../../lib/i18n';
  import Icon from '../../lib/components/Icon.svelte';
  import Segmented from '../../lib/components/Segmented.svelte';
  import { ago, bytes, duration } from '../../lib/util/format';
  import { flip } from 'svelte/animate';
  import { fade } from 'svelte/transition';
  import type { ActivityLog, InstanceDetails } from '../../lib/types';

  let { details }: { details: InstanceDetails } = $props();

  let activity = $state<ActivityLog | null>(null);
  let size = $state<{ worlds: number; mods: number; rest: number } | null>(null);
  let mode = $state<'month' | 'year'>('year');
  let monthOffset = $state(0);
  let hover = $state<string | null>(null);

  onMount(async () => {
    const id = details.instance.id;
    try {
      activity = await call<ActivityLog>('instances.activity', { id });
    } catch {
      activity = null;
    }
    try {
      size = await call('instances.size', { id });
    } catch {
      size = null;
    }
    try {
      places = await call<Place[]>('instances.places', { id });
    } catch {
      places = [];
    }
  });

  // Wo gespielt wurde (Server und Einzelspieler-Welten), unabhängig von der Serverliste
  type Place = { kind: 'world' | 'server'; id: string; name: string; iconUrl: string | null; seconds: number; lastPlayed: string | null; exists: boolean };
  let places = $state<Place[]>([]);
  let placeFilter = $state<'all' | 'world' | 'server'>('all');
  const shownPlaces = $derived(places.filter((p) => placeFilter === 'all' || p.kind === placeFilter));
  const maxPlace = $derived(Math.max(1, ...shownPlaces.map((p) => p.seconds)));

  const days = $derived(activity?.days ?? {});
  const key = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  const minutesOf = (d: Date) => Math.round((days[key(d)] ?? 0) / 60);
  const LEVELS = ['#333333', '#4a2647', '#7a2f73', '#b03d9e', '#e865c4'];
  const level = (m: number) => (m <= 0 ? 0 : m < 30 ? 1 : m < 90 ? 2 : m < 180 ? 3 : 4);
  const fmt = (m: number) => (m === 0 ? '–' : duration(m * 60));
  const dateLabel = (d: Date) => d.toLocaleDateString('de-DE', { weekday: 'short', day: 'numeric', month: 'long' });

  const today = new Date();
  today.setHours(0, 0, 0, 0);

  const week = $derived.by(() => {
    const result: { day: string; minutes: number }[] = [];
    let total = 0;
    let previous = 0;
    for (let i = 6; i >= 0; i--) {
      const d = new Date(today);
      d.setDate(d.getDate() - i);
      const m = minutesOf(d);
      total += m;
      result.push({ day: d.toLocaleDateString('de-DE', { weekday: 'short' }), minutes: m });
    }
    for (let i = 13; i >= 7; i--) {
      const d = new Date(today);
      d.setDate(d.getDate() - i);
      previous += minutesOf(d);
    }
    return { bars: result, total, diff: total - previous };
  });

  // Jahres-Heatmap: 53 Wochen, Montag oben
  const weeks = $derived.by(() => {
    const start = new Date(today);
    start.setDate(start.getDate() - 7 * 52 - ((today.getDay() + 6) % 7));
    const result: { label: string; days: { key: string; date: Date; minutes: number; future: boolean }[] }[] = [];
    const cursor = new Date(start);
    let lastMonth = -1;
    for (let w = 0; w < 53; w++) {
      const daysOfWeek = [];
      let label = '';
      for (let d = 0; d < 7; d++) {
        const date = new Date(cursor);
        if (d === 0 && date.getMonth() !== lastMonth) {
          label = date.toLocaleDateString('de-DE', { month: 'short' }).replace('.', '');
          lastMonth = date.getMonth();
        }
        daysOfWeek.push({ key: key(date), date, minutes: minutesOf(date), future: date > today });
        cursor.setDate(cursor.getDate() + 1);
      }
      result.push({ label, days: daysOfWeek });
    }
    return result;
  });

  const month = $derived.by(() => {
    const first = new Date(today.getFullYear(), today.getMonth() + monthOffset, 1);
    const lead = (first.getDay() + 6) % 7;
    const count = new Date(first.getFullYear(), first.getMonth() + 1, 0).getDate();
    const cells: ({ key: string; date: Date; minutes: number; future: boolean } | null)[] = Array(lead).fill(null);
    let total = 0;
    for (let d = 1; d <= count; d++) {
      const date = new Date(first.getFullYear(), first.getMonth(), d);
      const m = minutesOf(date);
      total += m;
      cells.push({ key: key(date), date, minutes: m, future: date > today });
    }
    while (cells.length % 7) cells.push(null);
    return { title: first.toLocaleDateString('de-DE', { month: 'long', year: 'numeric' }), cells, total };
  });

  const side = $derived.by(() => {
    const entries = Object.entries(days).filter(([, s]) => s > 0);
    const playDays = entries.length;
    const totalMin = entries.reduce((a, [, s]) => a + s / 60, 0);
    let streak = 0;
    let best = 0;
    const sorted = entries.map(([k]) => k).sort();
    let prev: Date | null = null;
    for (const k of sorted) {
      const d = new Date(k);
      streak = prev && (d.getTime() - prev.getTime()) / 86400000 === 1 ? streak + 1 : 1;
      best = Math.max(best, streak);
      prev = d;
    }
    const weekday = [0, 0, 0, 0, 0, 0, 0];
    for (const [k, s] of entries) weekday[(new Date(k).getDay() + 6) % 7] += s;
    const bestDay = weekday.indexOf(Math.max(...weekday));
    const names = ['Montag', 'Dienstag', 'Mittwoch', 'Donnerstag', 'Freitag', 'Samstag', 'Sonntag'];
    return {
      days: playDays,
      avg: playDays ? fmt(Math.round(totalMin / playDays)) : '–',
      streak: best ? t('instance.overview.streakDays', { count: best }) : '–',
      weekday: playDays ? names[bestDay] : '–'
    };
  });

  const hoverText = $derived.by(() => {
    if (!hover) return mode === 'month' ? t('instance.overview.monthTotal', { value: fmt(month.total) }) : '';
    const d = new Date(hover);
    return `${dateLabel(d)}: ${fmt(Math.round((days[hover] ?? 0) / 60))}`;
  });

  const lastPlayed = $derived.by(() => {
    if (!activity?.lastPlayed) return t('common.never');
    const d = new Date(activity.lastPlayed);
    const isToday = key(d) === key(today);
    return isToday
      ? t('instance.overview.today', { time: d.toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' }) })
      : d.toLocaleDateString('de-DE', { day: 'numeric', month: 'short' });
  });

  const totalSize = $derived(size ? size.worlds + size.mods + size.rest : 0);
  const maxWeek = $derived(Math.max(60, ...week.bars.map((b) => b.minutes)));

  // Heatmap und Kalender wachsen mit dem Platz: Zellgröße aus Breite und Höhe des Diagrammbereichs
  let chartW = $state(0);
  const heat = $derived.by(() => {
    const byWidth = (chartW - 28) / 53 / 1.2;
    // Nur nach der Breite: das Feld wächst nicht mehr bis zum Seitenende
    const cell = Math.max(6, Math.min(18, Math.floor(byWidth)));
    return { cell, gap: Math.max(2, Math.round(cell * 0.2)) };
  });
  const dayHeight = $derived.by(() => {
    const rows = month.cells.length / 7;
    void rows;
    return Math.max(30, Math.min(64, Math.floor((chartW - 36) / 7 * 0.6)));
  });
</script>

<div class="overview">
  <div class="stats">
    <article class="stat">
      <div class="stat-head"><span class="stat-icon"><Icon name="clock" /></span>{t('instance.overview.total')}</div>
      <span class="big">{activity ? t('common.hours', { count: Math.round(activity.totalSeconds / 3600) }) : '–'}</span>
      <span class="small">{activity?.firstPlayed ? t('instance.overview.since', { date: new Date(activity.firstPlayed).toLocaleDateString('de-DE', { day: 'numeric', month: 'long', year: 'numeric' }) }) : t('instance.overview.notPlayed')}</span>
    </article>
    <article class="stat">
      <div class="stat-head"><span class="stat-icon"><Icon name="stats" /></span>{t('instance.overview.week')}</div>
      <div class="week-row">
        <span class="big">{fmt(week.total)}</span>
        <div class="bars" aria-hidden="true">
          {#each week.bars as b, i (i)}
            <span style="height: {Math.max(3, (b.minutes / maxWeek) * 30)}px; background: {b.minutes ? 'var(--accent)' : '#3a3a3a'}"></span>
          {/each}
        </div>
      </div>
      <span class="small">{week.diff >= 0 ? '+' : '−'}{fmt(Math.abs(week.diff))} {t('instance.overview.vsLastWeek')}</span>
    </article>
    <article class="stat">
      <div class="stat-head"><span class="stat-icon"><Icon name="play" size={14} /></span>{t('instance.overview.lastPlayed')}</div>
      <span class="big">{lastPlayed}</span>
      <span class="small">{activity?.lastSessionSeconds ? t('instance.overview.session', { value: duration(activity.lastSessionSeconds) }) : ''}</span>
    </article>
    <article class="stat">
      <div class="stat-head"><span class="stat-icon"><Icon name="folder" /></span>{t('instance.overview.size')}</div>
      <span class="big">{size ? bytes(totalSize) : '–'}</span>
      {#if size && totalSize > 0}
        <div class="size-bar" aria-hidden="true">
          <span style="width: {(size.worlds / totalSize) * 100}%; background: var(--accent-from)"></span>
          <span style="width: {(size.mods / totalSize) * 100}%; background: var(--accent-to)"></span>
          <span class="grow" style="background: #4a4a4a"></span>
        </div>
        <span class="small">{t('instance.overview.sizeParts', { worlds: bytes(size.worlds), mods: bytes(size.mods), rest: bytes(size.rest) })}</span>
      {/if}
    </article>
  </div>

  <div class="lower">
  <section class="activity panel" aria-label={t('instance.overview.activity')}>
    <div class="act-head">
      <h2>{t('instance.overview.activity')}</h2>
      <div class="grow"></div>
      {#if mode === 'month'}
        <div class="month-nav">
          <button class="icon-btn" aria-label={t('instance.overview.prevMonth')} onclick={() => monthOffset--}><Icon name="chevronLeft" /></button>
          <span class="month-title">{month.title}</span>
          <button class="icon-btn" aria-label={t('instance.overview.nextMonth')} disabled={monthOffset >= 0} onclick={() => monthOffset++}><Icon name="chevronRight" /></button>
        </div>
      {/if}
      <Segmented bind:value={mode} label={t('instance.overview.range')} options={[{ value: 'month', label: t('instance.overview.month') }, { value: 'year', label: t('instance.overview.year') }]} />
    </div>
    <div class="act-body">
      <div class="chart" bind:clientWidth={chartW}>
        {#if mode === 'year'}
          <div class="heat" style="--cell: {heat.cell}px; --gap: {heat.gap}px">
            <div class="weekdays" aria-hidden="true"><span></span><span>Mo</span><span></span><span>Mi</span><span></span><span>Fr</span><span></span></div>
            <div class="weeks-wrap">
              <div class="month-labels" aria-hidden="true">
                {#each weeks as w, i (i)}<span>{w.label}</span>{/each}
              </div>
              <div class="weeks">
                {#each weeks as w, i (i)}
                  <div class="week">
                    {#each w.days as d (d.key)}
                      <!-- svelte-ignore a11y_no_static_element_interactions -->
                      <span
                        class="cell"
                        class:future={d.future}
                        class:today={d.key === key(today)}
                        style="background: {d.future ? 'transparent' : LEVELS[level(d.minutes)]}"
                        title={d.future ? '' : `${dateLabel(d.date)} · ${fmt(d.minutes)}`}
                        onmouseenter={() => !d.future && (hover = d.key)}
                        onmouseleave={() => (hover = null)}></span>
                    {/each}
                  </div>
                {/each}
              </div>
            </div>
          </div>
        {:else}
          <div class="cal" style="--day: {dayHeight}px">
            {#each ['Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa', 'So'] as d (d)}<span class="dow">{d}</span>{/each}
            {#each month.cells as c, i (i)}
              {#if c}
                {@const lv = level(c.minutes)}
                <!-- svelte-ignore a11y_no_static_element_interactions -->
                <div
                  class="day"
                  class:future={c.future}
                  class:today={c.key === key(today)}
                  style="background: {c.future ? 'transparent' : LEVELS[lv]}"
                  title={c.future ? '' : `${dateLabel(c.date)} · ${fmt(c.minutes)}`}
                  onmouseenter={() => !c.future && (hover = c.key)}
                  onmouseleave={() => (hover = null)}>
                  <span class="num" style="color: {c.future ? '#4f4f4f' : lv >= 3 ? '#fff' : lv ? '#e0c6e6' : '#8a8a8a'}">{c.date.getDate()}</span>
                  {#if !c.future && c.minutes > 0}<span class="val">{c.minutes < 60 ? `${c.minutes}m` : `${Math.round(c.minutes / 60)}h`}</span>{/if}
                </div>
              {:else}
                <span></span>
              {/if}
            {/each}
          </div>
        {/if}
        <div class="legend">
          <span class="hover">{hoverText}</span>
          <span>{t('instance.overview.less')}</span>
          <div class="levels" aria-hidden="true">{#each LEVELS as l (l)}<span style="background: {l}"></span>{/each}</div>
          <span>{t('instance.overview.more')}</span>
        </div>
      </div>
      <div class="side">
        <div><span class="lbl">{t('instance.overview.playDays')}</span><span class="val2">{side.days}</span></div>
        <div><span class="lbl">{t('instance.overview.avg')}</span><span class="val2">{side.avg}</span></div>
        <div><span class="lbl">{t('instance.overview.streak')}</span><span class="val2">{side.streak}</span></div>
        <div><span class="lbl">{t('instance.overview.weekday')}</span><span class="val2">{side.weekday}</span></div>
      </div>
    </div>
  </section>

  <!-- So hoch wie die Aktivität daneben, längere Listen scrollen -->
  <div class="places-slot">
  <section class="places panel" aria-label={t('instance.overview.places')}>
    <div class="act-head">
      <h2>{t('instance.overview.places')}</h2>
      <div class="grow"></div>
      <Segmented bind:value={placeFilter} label={t('instance.overview.placesFilter')} options={[{ value: 'all', label: t('instance.overview.placesAll') }, { value: 'world', label: t('instance.overview.placesSingle') }, { value: 'server', label: t('instance.overview.placesMulti') }]} />
    </div>
    <div class="place-list">
      {#each shownPlaces as p (`${p.kind}:${p.id}`)}
        <div class="place" class:gone={!p.exists} animate:flip={{ duration: 220 }} in:fade={{ duration: 160 }}>
          <span class="p-icon" class:world={p.kind === 'world'}>
            {#if p.iconUrl}<img src={p.iconUrl} alt="" />{:else}<Icon name={p.kind === 'world' ? 'map' : 'servers'} size={16} />{/if}
          </span>
          <div class="p-text">
            <span class="p-name truncate">{p.name}</span>
            <span class="p-sub truncate">
              {p.kind === 'world' ? t('instance.overview.placesSingle') : p.name !== p.id ? p.id : t('instance.overview.placesMulti')}{!p.exists ? ` · ${t('instance.overview.placeDeleted')}` : ''}{p.lastPlayed ? ` · ${ago(p.lastPlayed)}` : ''}
            </span>
            <span class="p-bar" aria-hidden="true"><span style="width: {(p.seconds / maxPlace) * 100}%"></span></span>
          </div>
          <span class="p-time">{duration(p.seconds)}</span>
        </div>
      {:else}
        <p class="p-empty">{placeFilter === 'world' ? t('instance.overview.placesEmptySingle') : placeFilter === 'server' ? t('instance.overview.placesEmptyMulti') : t('instance.overview.placesEmpty')}</p>
      {/each}
    </div>
  </section>
  </div>
  </div>
</div>

<style>
  .overview {
    flex-grow: 1;
    min-height: 0;
    overflow-y: auto;
    display: flex;
    flex-direction: column;
    gap: 16px;
  }
  .stats {
    display: grid;
    grid-template-columns: repeat(4, minmax(0, 1fr));
    gap: 16px;
    flex-shrink: 0;
  }
  .stat {
    padding: 16px;
    border-radius: 12px;
    background: var(--panel);
    display: flex;
    flex-direction: column;
    gap: 10px;
  }
  .stat-head {
    display: flex;
    align-items: center;
    gap: 10px;
    font-size: 13px;
    font-weight: 500;
    color: var(--muted);
  }
  .stat-icon {
    width: 30px;
    height: 30px;
    border-radius: 8px;
    background: var(--accent-soft-strong);
    color: var(--accent-text);
    display: flex;
    align-items: center;
    justify-content: center;
  }
  .big {
    font-size: 26px;
    font-weight: 700;
    color: var(--text-strong);
    letter-spacing: -0.4px;
  }
  .small {
    font-size: 12px;
    color: var(--muted-2);
    min-height: 15px;
  }
  .week-row {
    display: flex;
    align-items: flex-end;
    justify-content: space-between;
    gap: 8px;
  }
  .bars {
    height: 30px;
    display: flex;
    align-items: flex-end;
    gap: 3px;
  }
  .bars span {
    width: 6px;
    border-radius: 2px;
  }
  .size-bar {
    height: 6px;
    border-radius: 3px;
    overflow: hidden;
    display: flex;
    gap: 2px;
  }
  /* Aktivität und Orte nebeneinander, je halbe Breite */
  .lower {
    flex-shrink: 0;
    display: grid;
    grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
    gap: 16px;
  }
  .activity,
  .places {
    min-height: 0;
    padding: 18px 20px;
    display: flex;
    flex-direction: column;
    gap: 14px;
  }
  .places-slot {
    position: relative;
    min-height: 260px;
  }
  .places {
    position: absolute;
    inset: 0;
  }
  .place-list {
    flex-grow: 1;
    min-height: 0;
    overflow-y: auto;
    margin-right: -8px;
    padding-right: 8px;
    display: flex;
    flex-direction: column;
    gap: 8px;
  }
  .place {
    padding: 9px 12px 9px 9px;
    border-radius: 10px;
    background: var(--panel-3);
    display: flex;
    align-items: center;
    gap: 12px;
  }
  .place.gone {
    opacity: 0.6;
  }
  .p-icon {
    width: 40px;
    height: 40px;
    flex-shrink: 0;
    border-radius: 7px;
    overflow: hidden;
    background: #3a3a3a;
    color: var(--muted-2);
    display: flex;
    align-items: center;
    justify-content: center;
  }
  .p-icon.world {
    width: 58px;
  }
  .p-icon img {
    width: 100%;
    height: 100%;
    object-fit: cover;
    image-rendering: pixelated;
  }
  .p-text {
    flex-grow: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: 3px;
  }
  .p-name {
    font-size: 13.5px;
    font-weight: 600;
  }
  .p-sub {
    font-size: 11.5px;
    color: var(--muted-2);
  }
  .p-bar {
    height: 4px;
    margin-top: 2px;
    border-radius: 2px;
    background: #3a3a3a;
    overflow: hidden;
  }
  .p-bar span {
    display: block;
    height: 100%;
    border-radius: 2px;
    background: var(--accent);
    transition: width 0.3s ease;
  }
  .p-time {
    flex-shrink: 0;
    min-width: 64px;
    text-align: right;
    font-size: 14px;
    font-weight: 700;
    font-variant-numeric: tabular-nums;
    color: var(--text-strong);
  }
  .p-empty {
    margin: auto;
    max-width: 280px;
    text-align: center;
    font-size: 13px;
    line-height: 1.5;
    color: var(--muted-2);
  }
  .act-head {
    display: flex;
    align-items: center;
    gap: 12px;
  }
  h2 {
    font-size: 16px;
    font-weight: 600;
  }
  .month-nav {
    display: flex;
    align-items: center;
    gap: 6px;
  }
  .month-title {
    width: 130px;
    text-align: center;
    font-size: 13px;
    font-weight: 600;
  }
  .act-body {
    flex-grow: 1;
    min-height: 0;
    display: flex;
    flex-direction: column;
    gap: 14px;
  }
  .chart {
    flex-grow: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
    justify-content: center;
    gap: 12px;
    overflow: hidden;
  }
  .heat {
    display: flex;
    gap: 6px;
  }
  .weekdays {
    width: 22px;
    padding-top: 19px;
    display: grid;
    grid-template-rows: repeat(7, var(--cell));
    row-gap: var(--gap);
    font-size: 10px;
    color: var(--faint);
    line-height: var(--cell);
  }
  .weeks-wrap {
    display: flex;
    flex-direction: column;
    gap: 6px;
    min-width: 0;
    overflow: hidden;
  }
  .month-labels {
    display: flex;
    gap: var(--gap);
    height: 13px;
    font-size: 10px;
    color: var(--faint);
  }
  .month-labels span {
    width: var(--cell);
    flex-shrink: 0;
    overflow: visible;
    white-space: nowrap;
  }
  .weeks {
    display: flex;
    gap: var(--gap);
  }
  .week {
    display: flex;
    flex-direction: column;
    gap: var(--gap);
  }
  .cell {
    width: var(--cell);
    height: var(--cell);
    border-radius: calc(var(--cell) * 0.22);
    flex-shrink: 0;
  }
  .cell.future {
    box-shadow: inset 0 0 0 1px #2e2e2e;
  }
  .cell.today,
  .day.today {
    box-shadow: inset 0 0 0 1.5px var(--accent-text);
  }
  .cal {
    width: 100%;
    display: grid;
    grid-template-columns: repeat(7, minmax(0, 1fr));
    gap: 6px;
  }
  .dow {
    padding-left: 9px;
    font-size: 11px;
    font-weight: 600;
    color: var(--faint);
  }
  .day {
    height: var(--day);
    padding: 0 10px;
    border-radius: 7px;
    display: flex;
    align-items: center;
    justify-content: space-between;
    font-variant-numeric: tabular-nums;
  }
  .day.future {
    box-shadow: inset 0 0 0 1px #2e2e2e;
  }
  .num {
    font-size: 12px;
    font-weight: 600;
  }
  .val {
    font-size: 11px;
    font-weight: 600;
    color: #f2d9f6;
  }
  .legend {
    display: flex;
    align-items: center;
    gap: 16px;
    font-size: 12px;
    color: var(--muted-2);
  }
  .hover {
    flex-grow: 1;
    color: #bdbdbd;
  }
  .levels {
    display: flex;
    gap: 3px;
  }
  .levels span {
    width: 13px;
    height: 13px;
    border-radius: 3px;
  }
  /* Kennzahlen bei halber Breite unter dem Diagramm */
  .side {
    flex-shrink: 0;
    padding-top: 12px;
    border-top: 1px solid var(--line);
    display: grid;
    grid-template-columns: repeat(4, minmax(0, 1fr));
    gap: 12px;
  }
  .side div {
    display: flex;
    flex-direction: column;
    gap: 3px;
  }
  .lbl {
    font-size: 12px;
    color: var(--muted-2);
  }
  .val2 {
    font-size: 18px;
    font-weight: 700;
    color: var(--text-strong);
  }
</style>
