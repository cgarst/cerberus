import React from 'react';
import { StyleSheet, Text, View } from 'react-native';
import { SystemMetrics } from '../api/models';
import { Colors, Radii } from '../theme';
import { MetricRingGauge } from './MetricRingGauge';
import { SparklineChart } from './SparklineChart';
import { SteamSurface } from './SteamSurface';

interface TelemetrySectionProps {
  metrics: SystemMetrics;
  layout?: 'stacked' | 'desktop';
  gaugesWidth?: number;
  alwaysShowGameStats?: boolean;
}

export function TelemetrySection({
  metrics,
  layout = 'stacked',
  gaugesWidth = 280,
  alwaysShowGameStats = false,
}: TelemetrySectionProps) {
  const gpuSubtitleParts: string[] = [];
  if (metrics.gpuTempC !== null && metrics.gpuTempC !== undefined) {
    gpuSubtitleParts.push(`${Math.round(metrics.gpuTempC)}°C`);
  }
  if (
    metrics.gpuVramUsedGb !== null &&
    metrics.gpuVramUsedGb !== undefined &&
    metrics.gpuVramTotalGb !== null &&
    metrics.gpuVramTotalGb !== undefined
  ) {
    gpuSubtitleParts.push(
      `${metrics.gpuVramUsedGb.toFixed(1)}/${Math.round(metrics.gpuVramTotalGb)}G`
    );
  }
  const gpuSubtitle = gpuSubtitleParts.join(' · ');

  const ramSubtitle =
    metrics.ramUsedGb !== null &&
    metrics.ramUsedGb !== undefined &&
    metrics.ramTotalGb !== null &&
    metrics.ramTotalGb !== undefined
      ? `${metrics.ramUsedGb.toFixed(1)}/${Math.round(metrics.ramTotalGb)}G`
      : '';

  const showFrameCard =
    alwaysShowGameStats ||
    (metrics.fps !== null && metrics.fps !== undefined) ||
    (metrics.frametimeMs !== null && metrics.frametimeMs !== undefined);

  const gaugesBlock = (
    <View
      style={[
        styles.columnBlock,
        layout === 'desktop' && { width: gaugesWidth },
      ]}
    >
      <Text style={styles.sectionHeader}>SYSTEM STATS</Text>
      <SteamSurface
        style={[
          styles.gaugesCard,
          layout === 'desktop' && styles.gaugesCardDesktop,
        ]}
      >
        <View style={styles.gaugesInner}>
          <MetricRingGauge
            title="CPU"
            modelName={metrics.cpuName ?? ''}
            percent={metrics.cpuPercent}
            subtitle={
              metrics.cpuTempC !== null && metrics.cpuTempC !== undefined
                ? `${Math.round(metrics.cpuTempC)}°C`
                : ''
            }
          />
          <MetricRingGauge
            title="GPU"
            modelName={metrics.gpuName ?? ''}
            percent={metrics.gpuPercent}
            subtitle={gpuSubtitle}
          />
          <MetricRingGauge
            title="RAM"
            modelName={metrics.ramName ?? ''}
            percent={metrics.ramPercent}
            subtitle={ramSubtitle}
          />
        </View>
      </SteamSurface>
    </View>
  );

  if (layout === 'desktop') {
    return (
      <View style={styles.desktopRow}>
        {gaugesBlock}
        {showFrameCard ? (
          <View style={[styles.columnBlock, styles.flexOne]}>
            <Text style={styles.sectionHeader}>GAME STATS</Text>
            <FramePerformanceCard
              metrics={metrics}
              sideBySideGraphs
            />
          </View>
        ) : null}
      </View>
    );
  }

  return (
    <View style={styles.section}>
      {gaugesBlock}
      {showFrameCard && (
        <View style={styles.columnBlock}>
          <Text style={styles.sectionHeader}>GAME STATS</Text>
          <FramePerformanceCard
            metrics={metrics}
          />
        </View>
      )}
    </View>
  );
}

function FramePerformanceCard({
  metrics,
  sideBySideGraphs = false,
}: {
  metrics: SystemMetrics;
  sideBySideGraphs?: boolean;
}) {
  const fpsHistory = metrics.fpsHistory ?? [];
  const validFps = fpsHistory.filter(
    (v): v is number => v !== null && v !== undefined && Number.isFinite(v)
  );
  const maxFps = validFps.length > 0 ? Math.max(...validFps) : 0;
  const fpsCeil = Math.max(60, Math.ceil((maxFps * 1.1) / 30) * 30);
  const minFps = validFps.length > 0 ? Math.min(...validFps) : 0;
  const avgFps =
    validFps.length > 0
      ? validFps.reduce((a, b) => a + b, 0) / validFps.length
      : 0;

  const ftHistory = metrics.frametimeHistoryMs ?? [];
  const validFt = ftHistory.filter(
    (v): v is number => v !== null && v !== undefined && Number.isFinite(v)
  );
  const peakFt = validFt.length > 0 ? Math.max(...validFt) : 0;
  const ftCeil = Math.max(20, Math.ceil((peakFt * 1.15) / 5) * 5);
  const avgFt =
    validFt.length > 0
      ? validFt.reduce((a, b) => a + b, 0) / validFt.length
      : 0;
  const ftColor =
    peakFt > 33.3
      ? Colors.red
      : peakFt > 16.7
      ? Colors.orange
      : Colors.steamGreenBright;

  return (
    <SteamSurface
      style={[styles.frameCard, sideBySideGraphs && styles.frameCardDesktop]}
    >
      <View
        style={[
          styles.frameInner,
          sideBySideGraphs && styles.frameInnerDesktop,
        ]}
      >
        <View style={sideBySideGraphs ? styles.graphsRow : styles.graphsCol}>
          <FrameMetricBlock
            label="FRAMERATE"
            value={
              metrics.fps !== null && metrics.fps !== undefined
                ? `${Math.round(metrics.fps)} fps`
                : '—'
            }
            subtitle={
              validFps.length > 0
                ? `min ${Math.round(minFps)} · avg ${Math.round(avgFps)}`
                : undefined
            }
            sideBySide={sideBySideGraphs}
            values={fpsHistory}
            maxValue={fpsCeil}
            strokeColor={Colors.steamBlue}
            referenceValue={null}
            scaleLabel={validFps.length > 0 ? `${Math.round(fpsCeil)}` : null}
          />

          <FrameMetricBlock
            label="FRAMETIME"
            value={
              metrics.frametimeMs !== null && metrics.frametimeMs !== undefined
                ? `${metrics.frametimeMs.toFixed(1)} ms`
                : '—'
            }
            subtitle={
              validFt.length > 0
                ? `avg ${avgFt.toFixed(1)} · peak ${peakFt.toFixed(1)} ms`
                : undefined
            }
            sideBySide={sideBySideGraphs}
            values={ftHistory}
            maxValue={ftCeil}
            strokeColor={ftColor}
            referenceValue={16.7}
            scaleLabel={validFt.length > 0 ? `${Math.round(ftCeil)} ms` : null}
          />
        </View>
      </View>
    </SteamSurface>
  );
}

function FrameMetricBlock({
  label,
  value,
  subtitle,
  sideBySide,
  values,
  maxValue,
  strokeColor,
  referenceValue,
  scaleLabel,
}: {
  label: string;
  value: string;
  subtitle?: string;
  sideBySide: boolean;
  values: (number | null)[];
  maxValue: number;
  strokeColor: string;
  referenceValue: number | null;
  scaleLabel: string | null;
}) {
  return (
    <View style={[styles.metricBlock, sideBySide && styles.flexOne]}>
      <View style={styles.metricHeader}>
        <View style={styles.metricRow}>
          <Text style={styles.metricLabel} numberOfLines={1}>
            {label}
          </Text>
          <Text style={styles.metricValue} numberOfLines={1}>
            {value}
          </Text>
        </View>
        {subtitle ? (
          <Text style={styles.metricSub} numberOfLines={1}>
            {subtitle}
          </Text>
        ) : null}
      </View>
      <SparklineChart
        values={values}
        maxValue={maxValue}
        strokeColor={strokeColor}
        referenceValue={referenceValue}
        scaleLabel={scaleLabel}
        style={sideBySide ? styles.sparklineFill : undefined}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  flexOne: { flex: 1, minWidth: 0 },
  section: {
    gap: 12,
  },
  desktopRow: {
    flexDirection: 'row',
    alignItems: 'stretch',
    gap: 12,
  },
  columnBlock: {
    gap: 8,
  },
  sectionHeader: {
    fontSize: 11,
    fontWeight: '700',
    color: Colors.textSecondary,
    paddingHorizontal: 2,
    letterSpacing: 0.9,
  },
  gaugesCard: {
    borderRadius: Radii.md,
  },
  gaugesCardDesktop: {
    flex: 1,
    justifyContent: 'center',
  },
  gaugesInner: {
    flexDirection: 'row',
    gap: 8,
    paddingVertical: 14,
    paddingHorizontal: 10,
  },
  frameCard: {
    borderRadius: Radii.md,
  },
  frameCardDesktop: {
    flex: 1,
  },
  frameInner: {
    padding: 14,
    gap: 12,
  },
  frameInnerDesktop: {
    flex: 1,
    gap: 10,
  },
  graphsCol: {
    gap: 12,
  },
  graphsRow: {
    flex: 1,
    flexDirection: 'row',
    gap: 12,
  },
  sparklineFill: {
    flex: 1,
  },
  metricBlock: {
    gap: 6,
    minWidth: 0,
  },
  metricHeader: {
    gap: 2,
    minWidth: 0,
  },
  metricRow: {
    flexDirection: 'row',
    alignItems: 'baseline',
    justifyContent: 'space-between',
    gap: 8,
    minWidth: 0,
  },
  metricLabel: {
    fontSize: 11,
    fontWeight: '700',
    color: Colors.textSecondary,
    letterSpacing: 0.6,
    flexShrink: 1,
  },
  metricSub: {
    fontSize: 11,
    color: Colors.textSecondary,
  },
  metricValue: {
    fontSize: 14,
    fontWeight: '700',
    color: Colors.textBright,
    flexShrink: 0,
  },
});
