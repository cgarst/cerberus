import React, { useMemo, useState } from 'react';
import {
  LayoutChangeEvent,
  StyleProp,
  StyleSheet,
  Text,
  View,
  ViewStyle,
} from 'react-native';
import Svg, { Line, Path } from 'react-native-svg';
import { Colors, Radii } from '../theme';

interface SparklineChartProps {
  values: (number | null)[];
  maxValue: number;
  strokeColor: string;
  referenceValue?: number | null;
  scaleLabel?: string | null;
  style?: StyleProp<ViewStyle>;
}

export function SparklineChart({
  values,
  maxValue,
  strokeColor,
  referenceValue,
  scaleLabel,
  style,
}: SparklineChartProps) {
  const [size, setSize] = useState({ width: 0, height: 0 });
  const padY = 5;

  const onLayout = (e: LayoutChangeEvent) => {
    const { width: w, height: h } = e.nativeEvent.layout;
    if (w > 0 && h > 0 && (w !== size.width || h !== size.height)) {
      setSize({ width: w, height: h });
    }
  };

  const { linePath, areaPath, refY } = useMemo(() => {
    const { width, height } = size;
    if (width <= 0 || height <= 0 || values.length === 0 || maxValue <= 0) {
      return { linePath: '', areaPath: '', refY: null as number | null };
    }

    const n = values.length;
    const plotH = Math.max(1, height - padY * 2);

    const lineCmds: string[] = [];
    const areaCmds: string[] = [];
    let segment: { x: number; y: number }[] = [];
    let inSegment = false;

    const flushArea = () => {
      if (segment.length >= 2 && n > 1) {
        const first = segment[0];
        const last = segment[segment.length - 1];
        areaCmds.push(`M ${first.x.toFixed(2)} ${first.y.toFixed(2)}`);
        for (let j = 1; j < segment.length; j++) {
          areaCmds.push(`L ${segment[j].x.toFixed(2)} ${segment[j].y.toFixed(2)}`);
        }
        areaCmds.push(`L ${last.x.toFixed(2)} ${height}`);
        areaCmds.push(`L ${first.x.toFixed(2)} ${height} Z`);
      }
      segment = [];
    };

    for (let i = 0; i < n; i++) {
      const v = values[i];
      if (v === null || v === undefined || !Number.isFinite(v)) {
        inSegment = false;
        flushArea();
        continue;
      }
      const x = n === 1 ? width : (i / (n - 1)) * width;
      const clamped = Math.max(0, Math.min(maxValue, v));
      const y = padY + plotH * (1 - clamped / maxValue);

      if (!inSegment) {
        lineCmds.push(`M ${x.toFixed(2)} ${y.toFixed(2)}`);
        inSegment = true;
      } else {
        lineCmds.push(`L ${x.toFixed(2)} ${y.toFixed(2)}`);
      }
      segment.push({ x, y });
    }
    flushArea();

    let computedRefY: number | null = null;
    if (
      referenceValue !== null &&
      referenceValue !== undefined &&
      referenceValue < maxValue
    ) {
      computedRefY = padY + plotH * (1 - referenceValue / maxValue);
    }

    return {
      linePath: lineCmds.join(' '),
      areaPath: areaCmds.join(' '),
      refY: computedRefY,
    };
  }, [size, values, maxValue, referenceValue]);

  return (
    <View style={[styles.container, style]} onLayout={onLayout}>
      {size.width > 0 && size.height > 0 && (
        <Svg
          width={size.width}
          height={size.height}
          style={StyleSheet.absoluteFill}
        >
          {refY !== null && (
            <Line
              x1={0}
              y1={refY}
              x2={size.width}
              y2={refY}
              stroke="rgba(255, 255, 255, 0.2)"
              strokeWidth={1}
              strokeDasharray="3,3"
            />
          )}
          {areaPath.length > 0 && (
            <Path d={areaPath} fill={strokeColor} fillOpacity={0.2} />
          )}
          {linePath.length > 0 && (
            <Path
              d={linePath}
              stroke={strokeColor}
              strokeWidth={1.8}
              strokeLinecap="round"
              strokeLinejoin="round"
              fill="none"
            />
          )}
        </Svg>
      )}
      {scaleLabel ? <Text style={styles.scaleLabel}>{scaleLabel}</Text> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    minHeight: 62,
    borderRadius: Radii.sm,
    backgroundColor: Colors.bgDeep,
    borderWidth: 1,
    borderColor: Colors.border,
    overflow: 'hidden',
    position: 'relative',
  },
  scaleLabel: {
    position: 'absolute',
    top: 3,
    right: 6,
    fontSize: 10,
    fontWeight: '600',
    color: Colors.textSecondary,
  },
});
