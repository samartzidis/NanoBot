import React, { useEffect, useId, useState } from 'react';
import { Card, CardContent, Typography, Box, SvgIcon } from '@mui/material';

import { apiBaseUrl } from '../config';

interface WifiStatus {
  interface: string;
  ssid: string | null;
  signalDbm: number | null;
  linkQuality: number | null;
  rating: string | null;
}

interface MemoryStatus {
  usedMb: number;
  totalMb: number;
  availableMb: number;
  swapUsedMb: number;
  swapTotalMb: number;
}

interface DiskStatus {
  usedGb: number;
  totalGb: number;
  freeGb: number;
}

interface SystemStatus {
  supported: boolean;
  wifi: WifiStatus | null;
  memory: MemoryStatus | null;
  cpuTemperatureCelsius: number | null;
  uptimeSeconds: number | null;
  disk: DiskStatus | null;
}

const POLL_INTERVAL_MS = 5000;

const formatUptime = (seconds: number): string => {
  const days = Math.floor(seconds / 86400);
  const hours = Math.floor((seconds % 86400) / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);
  return `${days}d ${hours}h ${minutes}m`;
};

// Signal level out of 4 for a rating string, the same scale as the cone icon.
const SIGNAL_LEVELS: Record<string, number> = { excellent: 4, good: 3, fair: 2, weak: 1 };

// Theme colour per signal level: green for excellent/good, amber for fair, red for weak.
const SIGNAL_COLORS: Record<number, 'success' | 'warning' | 'error'> = { 4: 'success', 3: 'success', 2: 'warning', 1: 'error' };

// Cone-shaped signal icon: a dimmed full cone with the first `level` quarters filled.
const SignalCone: React.FC<{ level: number }> = ({ level }) => {
  const clipId = useId();
  const cone = 'M2 22h20V2z';
  return (
    <SvgIcon color={SIGNAL_COLORS[level] ?? 'disabled'} sx={{ mr: 1 }}>
      <clipPath id={clipId}>
        <rect x="2" y="0" width={(20 * level) / 4} height="24" />
      </clipPath>
      <path d={cone} opacity={0.3} />
      <path d={cone} clipPath={`url(#${clipId})`} />
    </SvgIcon>
  );
};

const StatusCard: React.FC<{ title: string; value: string; detail?: string; icon?: React.ReactNode }> = ({ title, value, detail, icon }) => (
  <Card sx={{ minWidth: 200, flex: '1 1 200px' }}>
    <CardContent>
      <Typography variant="overline" color="text.secondary">{title}</Typography>
      <Box sx={{ display: 'flex', alignItems: 'center' }}>
        {icon}
        <Typography variant="h5">{value}</Typography>
      </Box>
      {detail && <Typography variant="body2" color="text.secondary">{detail}</Typography>}
    </CardContent>
  </Card>
);

const SystemStatusCards: React.FC = () => {
  const [status, setStatus] = useState<SystemStatus | null>(null);

  useEffect(() => {
    let cancelled = false;

    const load = async () => {
      try {
        const response = await fetch(`${apiBaseUrl}/api/System/GetStatus`);
        if (!response.ok) return;
        const data: SystemStatus = await response.json();
        if (!cancelled) setStatus(data);
      } catch (error) {
        console.error('Error fetching system status:', error);
      }
    };

    load();
    const timer = setInterval(load, POLL_INTERVAL_MS);
    return () => {
      cancelled = true;
      clearInterval(timer);
    };
  }, []);

  if (!status?.supported) return null;

  const { wifi, memory, cpuTemperatureCelsius, uptimeSeconds, disk } = status;

  return (
    <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 2, width: '100%' }}>
      {wifi && (
        <StatusCard
          title="Wi-Fi"
          value={wifi.signalDbm != null ? `${wifi.rating} (${Math.round(wifi.signalDbm)} dBm)` :
            wifi.linkQuality != null ? `Link quality ${Math.round(wifi.linkQuality)}/70` : 'Unknown'}
          detail={wifi.ssid ?? wifi.interface}
          icon={wifi.rating != null ? <SignalCone level={SIGNAL_LEVELS[wifi.rating] ?? 0} /> : undefined}
        />
      )}
      {memory && (
        <StatusCard
          title="Memory"
          value={`${Math.round((100 * memory.usedMb) / memory.totalMb)}%`}
          detail={`${memory.usedMb} of ${memory.totalMb} MB used` +
            (memory.swapTotalMb > 0 ? `, swap ${memory.swapUsedMb} of ${memory.swapTotalMb} MB` : '')}
        />
      )}
      {cpuTemperatureCelsius != null && (
        <StatusCard title="CPU temperature" value={`${cpuTemperatureCelsius.toFixed(1)} °C`} />
      )}
      {uptimeSeconds != null && <StatusCard title="Uptime" value={formatUptime(uptimeSeconds)} />}
      {disk && (
        <StatusCard
          title="Disk"
          value={`${Math.round((100 * disk.usedGb) / disk.totalGb)}%`}
          detail={`${disk.usedGb.toFixed(1)} of ${disk.totalGb.toFixed(1)} GB used, ${disk.freeGb.toFixed(1)} GB free`}
        />
      )}
    </Box>
  );
};

export default SystemStatusCards;
