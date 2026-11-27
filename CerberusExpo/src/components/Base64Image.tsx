import React, { useState } from 'react';
import { Image, StyleSheet, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { Colors } from '../theme';

interface Base64ImageProps {
  dataUrl?: string | null;
  size?: number;
  fallbackIcon?: keyof typeof Ionicons.glyphMap;
  fallbackColor?: string;
}

export function Base64Image({
  dataUrl,
  size = 24,
  fallbackIcon = 'apps',
  fallbackColor = Colors.steamBlue,
}: Base64ImageProps) {
  const [failed, setFailed] = useState(false);

  const normalizedUri = React.useMemo(() => {
    if (!dataUrl || dataUrl.trim().length === 0) return null;
    if (dataUrl.startsWith('data:')) return dataUrl;
    return `data:image/png;base64,${dataUrl}`;
  }, [dataUrl]);

  if (!normalizedUri || failed) {
    return (
      <View
        style={[
          styles.fallback,
          { width: size, height: size, borderRadius: 6 },
        ]}
      >
        <Ionicons
          name={fallbackIcon}
          size={Math.round(size * 0.72)}
          color={fallbackColor}
        />
      </View>
    );
  }

  return (
    <Image
      source={{ uri: normalizedUri }}
      style={{ width: size, height: size, borderRadius: 6 }}
      resizeMode="contain"
      onError={() => setFailed(true)}
    />
  );
}

const styles = StyleSheet.create({
  fallback: {
    alignItems: 'center',
    justifyContent: 'center',
  },
});
