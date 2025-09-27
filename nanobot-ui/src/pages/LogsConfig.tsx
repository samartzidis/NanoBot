import React, { useEffect, useState, useRef } from 'react';
import {
  Container,
  Typography,
  Box,
  Card,
  CardContent,
  Button,
  Stack,
  Alert,
  CircularProgress,
  Chip,
  Paper
} from '@mui/material';
import {
  PlayArrow as PlayIcon,
  Stop as StopIcon,
  Refresh as RefreshIcon,
  Clear as ClearIcon,
  Download as DownloadIcon
} from '@mui/icons-material';
import { apiBaseUrl } from '../config';

// Simplified - just store raw log lines as strings

interface LogsResponse {
  fileName: string | null;
  lines: string[];
  totalLines: number;
  hasNewLines: boolean;
  fileChanged: boolean;
  newPosition: number;
}

const LogsConfig: React.FC = () => {
  const [logs, setLogs] = useState<string[]>([]);
  const [isPolling, setIsPolling] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [fileName, setFileName] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [autoScroll, setAutoScroll] = useState(true);
  
  const pollingIntervalRef = useRef<NodeJS.Timeout | null>(null);
  const logsEndRef = useRef<HTMLDivElement>(null);
  const lastPositionRef = useRef<number>(0);
  const lastFileNameRef = useRef<string | null>(null);

  // No parsing needed - just use raw lines

  const fetchLogs = async (lastPosition: number = 0): Promise<LogsResponse | null> => {
    try {
      const url = `${apiBaseUrl}/api/System/GetLogs?lastPosition=${lastPosition}&lastFile=${encodeURIComponent(lastFileNameRef.current || '')}`;
      const response = await fetch(url);
      if (!response.ok) {
        throw new Error(`Failed to fetch logs: ${response.statusText}`);
      }
      return await response.json();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to fetch logs');
      return null;
    }
  };

  const startPolling = async () => {
    if (isPolling) return;

    setIsPolling(true);
    setIsLoading(true);
    setError(null);
    setLogs([]);
    lastPositionRef.current = 0;
    lastFileNameRef.current = null;

    // Initial fetch
    const initialData = await fetchLogs(0);
    if (initialData) {
      setFileName(initialData.fileName);
      setLogs(initialData.lines);
      lastPositionRef.current = initialData.newPosition;
      lastFileNameRef.current = initialData.fileName;
    }
    setIsLoading(false);

    // Start polling for new logs with 1-second interval
    pollingIntervalRef.current = setInterval(async () => {
      const data = await fetchLogs(lastPositionRef.current);
      if (data) {
        // Handle file rotation
        if (data.fileChanged) {
          setFileName(data.fileName);
          setLogs(data.lines); // Replace all logs with new file content
          lastPositionRef.current = data.newPosition;
          lastFileNameRef.current = data.fileName;
        } else if (data.hasNewLines && data.lines.length > 0) {
          // Add new lines to existing logs
          setLogs(prev => [...prev, ...data.lines]);
          lastPositionRef.current = data.newPosition;
        }
      }
    }, 3000); // Poll every 3 seconds
  };

  const stopPolling = () => {
    if (pollingIntervalRef.current) {
      clearInterval(pollingIntervalRef.current);
      pollingIntervalRef.current = null;
    }
    setIsPolling(false);
    setError(null);
  };

  const refreshLogs = async () => {
    setIsLoading(true);
    setError(null);
    
    const data = await fetchLogs(0);
    if (data) {
      setFileName(data.fileName);
      setLogs(data.lines);
      lastPositionRef.current = data.newPosition;
      lastFileNameRef.current = data.fileName;
    }
    setIsLoading(false);
  };

  const clearLogs = () => {
    setLogs([]);
    lastPositionRef.current = 0;
    lastFileNameRef.current = null;
  };

  const scrollToBottom = () => {
    logsEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  };

  const downloadLogs = () => {
    const logText = logs.join('\n');
    const blob = new Blob([logText], { type: 'text/plain' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName ? `logs-${fileName}` : 'logs.txt';
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  };


  // Auto-scroll to bottom when new logs arrive
  useEffect(() => {
    if (autoScroll) {
      scrollToBottom();
    }
  }, [logs, autoScroll]);

  // Cleanup on unmount
  useEffect(() => {
    return () => {
      if (pollingIntervalRef.current) {
        clearInterval(pollingIntervalRef.current);
      }
    };
  }, []);

  return (
    <Container maxWidth="xl">
      <Box sx={{ mb: 3 }}>
        <Typography variant="h4" component="h1" gutterBottom>
          📋 System Logs
        </Typography>
        <Typography variant="body1" color="text.secondary">
          Real-time log monitoring with polling
        </Typography>
      </Box>

      {error && (
        <Alert severity="error" sx={{ mb: 2 }} onClose={() => setError(null)}>
          {error}
        </Alert>
      )}

      {/* Controls */}
      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Stack direction="row" spacing={2} alignItems="center" sx={{ mb: 2 }}>
            <Button
              variant="contained"
              startIcon={isPolling ? <StopIcon /> : <PlayIcon />}
              onClick={isPolling ? stopPolling : startPolling}
              color={isPolling ? 'error' : 'primary'}
              disabled={isLoading}
            >
              {isPolling ? 'Stop Polling' : 'Start Polling'}
            </Button>

            <Button
              variant="outlined"
              startIcon={<RefreshIcon />}
              onClick={refreshLogs}
              disabled={isLoading}
            >
              Refresh
            </Button>

            <Button
              variant="outlined"
              startIcon={<ClearIcon />}
              onClick={clearLogs}
              disabled={isPolling}
            >
              Clear
            </Button>

            <Button
              variant="outlined"
              startIcon={<DownloadIcon />}
              onClick={downloadLogs}
              disabled={logs.length === 0}
            >
              Download
            </Button>

            <Box sx={{ flexGrow: 1 }} />

            <Chip
              label={isPolling ? 'Polling Active' : 'Stopped'}
              color={isPolling ? 'success' : 'default'}
              size="small"
            />

            {fileName && (
              <Chip
                label={`File: ${fileName}`}
                variant="outlined"
                size="small"
              />
            )}
          </Stack>
        </CardContent>
      </Card>

      {/* Logs Display */}
      <Card>
        <CardContent>
          <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 2 }}>
            <Typography variant="h6">
              Logs ({logs.length})
            </Typography>
            <Stack direction="row" spacing={1} alignItems="center">
              <Button
                size="small"
                onClick={() => {
                  setAutoScroll(!autoScroll);
                  if (!autoScroll) {
                    scrollToBottom();
                  }
                }}
                variant={autoScroll ? 'contained' : 'outlined'}
              >
                Auto-scroll {autoScroll ? 'ON' : 'OFF'}
              </Button>
              <Button
                size="small"
                onClick={scrollToBottom}
                disabled={logs.length === 0}
              >
                Scroll to Bottom
              </Button>
            </Stack>
          </Box>

          {isLoading && (
            <Box sx={{ display: 'flex', justifyContent: 'center', p: 3 }}>
              <CircularProgress />
            </Box>
          )}

          {logs.length === 0 && !isLoading && (
            <Box sx={{ textAlign: 'center', p: 3 }}>
              <Typography variant="h6" color="text.secondary">
                No logs available. Click "Start Polling" to begin.
              </Typography>
            </Box>
          )}

          {logs.length > 0 && (
            <Paper 
              variant="outlined" 
              sx={{ 
                height: 600, 
                overflow: 'auto', 
                backgroundColor: '#ffffff',
                color: '#000000',
                fontFamily: 'monospace',
                fontSize: '0.875rem',
                lineHeight: 1.4
              }}
            >
              {logs.map((log, index) => (
                <Box key={index} sx={{ p: 1 }}>
                  <Typography
                    variant="body2"
                    sx={{ 
                      wordBreak: 'break-word',
                      whiteSpace: 'pre-wrap',
                      fontFamily: 'monospace',
                      fontSize: '0.875rem',
                      lineHeight: 1.4,
                      color: '#000000'
                    }}
                  >
                    {log}
                  </Typography>
                </Box>
              ))}
              <div ref={logsEndRef} />
            </Paper>
          )}
        </CardContent>
      </Card>
    </Container>
  );
};

export default LogsConfig;