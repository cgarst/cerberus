import React from 'react';
import { StyleSheet, Text, View } from 'react-native';
import Svg, { Circle } from 'react-native-svg';
import { Colors } from '../theme';

interface MetricRingGaugeProps {
  title: string;
  modelName: string;
  percent?: number | null;
  subtitle: string;
}

export function MetricRingGauge({
  title,
  modelName,
  percent,
  subtitle,
}: MetricRingGaugeProps) {
  const clamped =
    percent !== null && percent !== undefined && Number.isFinite(percent)
      ? Math.max(0, Math.min(100, percent))
      : 0;

  const ringColor =
    clamped >= 90
      ? Colors.red
      : clamped >= 75
      ? Colors.orange
      : Colors.steamBlue;

  const size = 62;
  const strokeWidth = 5;
  const radius = (size - strokeWidth) / 2;
  const circumference = 2 * Math.PI * radius;
  const strokeDashoffset = circumference * (1 - clamped / 100);

  const percentText =
    percent !== null && percent !== undefined && Number.isFinite(percent)
      ? `${Math.round(percent)}%`
      : '—';

  return (
    <View style={styles.container}>
      <Text
        style={styles.modelName}
        numberOfLines={1}
        adjustsFontSizeToFit
        minimumFontScale={0.75}
        ellipsizeMode="tail"
      >
        {modelName || ' '}
      </Text>

      <View style={styles.ringWrapper}>
        <Svg width={size} height={size}>
          <Circle
            cx={size / 2}
            cy={size / 2}
            r={radius}
            stroke="rgba(255, 255, 255, 0.1)"
            strokeWidth={strokeWidth}
            fill="none"
          />
          {clamped > 0 && (
            <Circle
              cx={size / 2}
              cy={size / 2}
              r={radius}
              stroke={ringColor}
              strokeWidth={strokeWidth}
              strokeDasharray={`${circumference} ${circumference}`}
              strokeDashoffset={strokeDashoffset}
              strokeLinecap="round"
              fill="none"
              transform={`rotate(-90 ${size / 2} ${size / 2})`}
            />
          )}
        </Svg>
        <View style={styles.percentOverlay}>
          <Text style={styles.percentText}>{percentText}</Text>
        </View>
      </View>

      <Text style={styles.title}>{title}</Text>
      <Text style={styles.subtitle} numberOfLines={1}>
        {subtitle || ' '}
      </Text>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    alignItems: 'center',
    gap: 5,
  },
  modelName: {
    fontSize: 10,
    color: Colors.textSecondary,
    textAlign: 'center',
    height: 16,
    lineHeight: 16,
  },
  ringWrapper: {
    width: 62,
    height: 62,
    alignItems: 'center',
    justifyContent: 'center',
  },
  percentOverlay: {
    position: 'absolute',
    top: 0,
    right: 0,
    bottom: 0,
    left: 0,
    alignItems: 'center',
    justifyContent: 'center',
  },
  percentText: {
    fontSize: 13,
    fontWeight: '700',
    color: Colors.textBright,
  },
  title: {
    fontSize: 11,
    fontWeight: '700',
    letterSpacing: 0.7,
    color: Colors.text,
  },
  subtitle: {
    fontSize: 11,
    color: Colors.textSecondary,
  },
});
