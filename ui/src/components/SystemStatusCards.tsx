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

interface ThermalReading {
  name: string;
  celsius: number;
}

interface SystemStatus {
  supported: boolean;
  wifi: WifiStatus | null;
  memory: MemoryStatus | null;
  cpuTemperatureCelsius: number | null;
  // Every thermal zone, the CPU one first.
  temperatures: ThermalReading[] | null;
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
  // Celsius per thermal zone name.
  temperatures: Record<string, number>;
}

// Categorical line colours, assigned in this fixed order (validated for colour-blind separation on the white card).
// Aqua and yellow are faint on white, so a multi-line chart always has a legend and the hover tooltip shows values.
const SERIES_COLORS = ['#2a78d6', '#eb6834', '#1baf7a', '#eda100', '#e87ba4', '#008300', '#6250d6', '#e34948'];

// Friendly names for the usual zone types ("cpu-thermal", "gpu-thermal", "ve-thermal", "ddr-thermal").
const THERMAL_LABELS: Record<string, string> = { cpu: 'CPU', gpu: 'GPU', ve: 'Video', ddr: 'Memory' };

const thermalLabel = (name: string): string => {
  const key = name.replace(/-thermal$/, '');
  return THERMAL_LABELS[key] ?? key.toUpperCase();
};

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

interface ChartSeries {
  label: string;
  value: (sample: HistorySample) => number | null;
}

const HistoryChart: React.FC<{
  title: string;
  history: HistorySample[];
  series: ChartSeries[];
  unit: string;
  min?: number;
  max?: number;
}> = ({ title, history, series, unit, min, max }) => {
  // A single line is named by the title; several need a legend.
  const showLegend = series.length > 1;
  return (
    <Card sx={{ minWidth: 300, flex: '1 1 300px' }}>
      <CardContent>
        <Typography variant="overline" color="text.secondary">{title}</Typography>
        <LineChart
          height={200}
          // Shown empty until the first sample arrives.
          loading={history.length === 0}
          xAxis={[{
            data: history.map(s => s.time),
            scaleType: 'time',
            valueFormatter: (d: Date) => d.toLocaleTimeString(),
            // With no samples there is no time range, so show the last minute rather than an arbitrary default.
            ...(history.length === 0 && { min: new Date(Date.now() - 60_000), max: new Date() }),
          }]}
          yAxis={[{ min, max, valueFormatter: (v: number) => `${Math.round(v)}` }]}
          series={series.map((s, i) => ({
            data: history.map(s.value),
            label: s.label,
            color: SERIES_COLORS[i],
            // A single sample has no line to draw, so show it as a point.
            showMark: history.length === 1,
            curve: 'linear',
            valueFormatter: (v: number | null) => (v == null ? '' : `${Math.round(v)} ${unit}`),
          }))}
          slotProps={{ legend: { hidden: !showLegend } }}
          margin={{ top: showLegend ? 40 : 10, right: 20, bottom: 30, left: 50 }}
        />
      </CardContent>
    </Card>
  );
};

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
          temperatures: Object.fromEntries((data.temperatures ?? []).map(t => [t.name, t.celsius])),
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

  if (status && !status.supported) return null;

  // Until the first response arrives, the graphs are shown empty so the page does not jump when data loads.
  const loading = status == null;
  const { wifi, memory, cpuTemperatureCelsius, temperatures, uptimeSeconds, disk } =
    status ?? { wifi: null, memory: null, cpuTemperatureCelsius: null, temperatures: null, uptimeSeconds: null, disk: null };

  // One line per thermal zone, in the server's order (CPU first) so each zone keeps its colour. The palette has eight
  // colours; further zones are left out rather than repeating a colour.
  const temperatureSeries: ChartSeries[] = (temperatures ?? []).slice(0, SERIES_COLORS.length).map(t => ({
    label: thermalLabel(t.name),
    value: s => s.temperatures[t.name] ?? null,
  }));

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
      {(loading || wifi?.signalDbm != null) && (
        <HistoryChart title="Wi-Fi signal" history={history} series={[{ label: 'Wi-Fi signal', value: s => s.signalDbm }]} unit="dBm" min={-100} max={-30} />
      )}
      {(loading || memory) && (
        <HistoryChart title="Memory used" history={history} series={[{ label: 'Memory used', value: s => s.memoryUsedPercent }]} unit="%" min={0} max={100} />
      )}
      {/* Only reported on the supported boards (Raspberry Pi, Orange Pi); the Pi starts throttling at 80 °C. */}
      {(loading || temperatureSeries.length > 0) && (
        <HistoryChart title="Temperature" history={history} series={temperatureSeries} unit="°C" min={20} max={90} />
      )}
    </Box>
  );
};

export default SystemStatusCards;
