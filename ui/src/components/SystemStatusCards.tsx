import React, { useEffect, useId, useState } from 'react';
import { Card, CardContent, Typography, Box, SvgIcon } from '@mui/material';
import { LineChart } from '@mui/x-charts/LineChart';

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

// History is kept in the browser only, so it starts empty on page load: one hour of samples.
const MAX_HISTORY_SAMPLES = (60 * 60 * 1000) / POLL_INTERVAL_MS;

interface HistorySample {
  time: Date;
  signalDbm: number | null;
  memoryUsedPercent: number | null;
  cpuTemperatureCelsius: number | null;
}

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

const HistoryChart: React.FC<{
  title: string;
  history: HistorySample[];
  value: (sample: HistorySample) => number | null;
  unit: string;
  min?: number;
  max?: number;
}> = ({ title, history, value, unit, min, max }) => (
  <Card sx={{ minWidth: 300, flex: '1 1 300px' }}>
    <CardContent>
      <Typography variant="overline" color="text.secondary">{title}</Typography>
      <LineChart
        height={200}
        xAxis={[{
          data: history.map(s => s.time),
          scaleType: 'time',
          valueFormatter: (d: Date) => d.toLocaleTimeString(),
        }]}
        yAxis={[{ min, max, valueFormatter: (v: number) => `${Math.round(v)}` }]}
        series={[{
          data: history.map(value),
          label: title,
          // A single sample has no line to draw, so show it as a point.
          showMark: history.length === 1,
          curve: 'linear',
          valueFormatter: (v: number | null) => (v == null ? '' : `${Math.round(v)} ${unit}`),
        }]}
        slotProps={{ legend: { hidden: true } }}
        margin={{ top: 10, right: 20, bottom: 30, left: 50 }}
      />
    </CardContent>
  </Card>
);

const SystemStatusCards: React.FC = () => {
  const [status, setStatus] = useState<SystemStatus | null>(null);
  const [history, setHistory] = useState<HistorySample[]>([]);

  useEffect(() => {
    let cancelled = false;

    const load = async () => {
      try {
        const response = await fetch(`${apiBaseUrl}/api/System/GetStatus`);
        if (!response.ok) return;
        const data: SystemStatus = await response.json();
        if (cancelled) return;
        setStatus(data);
        setHistory(previous => [...previous, {
          time: new Date(),
          signalDbm: data.wifi?.signalDbm ?? null,
          memoryUsedPercent: data.memory ? (100 * data.memory.usedMb) / data.memory.totalMb : null,
          cpuTemperatureCelsius: data.cpuTemperatureCelsius,
        }].slice(-MAX_HISTORY_SAMPLES));
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
      {history.length > 0 && wifi?.signalDbm != null && (
        <HistoryChart title="Wi-Fi signal" history={history} value={s => s.signalDbm} unit="dBm" min={-100} max={-30} />
      )}
      {history.length > 0 && memory && (
        <HistoryChart title="Memory used" history={history} value={s => s.memoryUsedPercent} unit="%" min={0} max={100} />
      )}
      {/* Only reported on a Raspberry Pi, which starts throttling at 80 °C. */}
      {history.length > 0 && cpuTemperatureCelsius != null && (
        <HistoryChart title="CPU temperature" history={history} value={s => s.cpuTemperatureCelsius} unit="°C" min={20} max={90} />
      )}
    </Box>
  );
};

export default SystemStatusCards;
