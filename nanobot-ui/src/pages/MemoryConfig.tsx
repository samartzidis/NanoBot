import React, { useEffect, useState, useCallback } from 'react';
import {
  Container,
  Typography,
  Box,
  Card,
  CardContent,
  Button,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Paper,
  Chip,
  IconButton,
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  DialogContentText,
  TextField,
  InputAdornment,
  Stack,
  Alert,
  CircularProgress,
  Tooltip,
  Checkbox,
  FormControlLabel,
  Grid
} from '@mui/material';
import {
  Delete as DeleteIcon,
  Search as SearchIcon,
  Refresh as RefreshIcon,
  Clear as ClearIcon,
  Memory as MemoryIcon,
  Storage as StorageIcon,
  TrendingUp as TrendingUpIcon,
  Schedule as ScheduleIcon
} from '@mui/icons-material';
import { apiBaseUrl } from '../config';

interface MemoryDto {
  key: string;
  content: string;
  createdAt: string;
  updatedAt: string;
  accessCount: number;
  lastAccessedAt: string | null;
  hasEmbedding: boolean;
}

interface MemoryListResponse {
  totalCount: number;
  memories: MemoryDto[];
}

interface MemoryStatsResponse {
  totalMemories: number;
  totalSizeBytes: number;
  memoriesWithEmbeddings: number;
  oldestMemory: string | null;
  newestMemory: string | null;
  mostAccessedCount: number;
  averageAccessCount: number;
  recentlyAccessed: number;
}

const MemoryConfig: React.FC = () => {
  const [memories, setMemories] = useState<MemoryDto[]>([]);
  const [stats, setStats] = useState<MemoryStatsResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [searchQuery, setSearchQuery] = useState('');
  const [selectedMemories, setSelectedMemories] = useState<Set<string>>(new Set());
  const [deleteDialogOpen, setDeleteDialogOpen] = useState(false);
  const [clearAllDialogOpen, setClearAllDialogOpen] = useState(false);
  const [memoryToDelete, setMemoryToDelete] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

  const fetchMemories = async () => {
    try {
      setLoading(true);
      setError(null);
      
      const response = await fetch(`${apiBaseUrl}/api/Memory`);
      if (!response.ok) {
        throw new Error(`Failed to fetch memories: ${response.statusText}`);
      }
      
      const data: MemoryListResponse = await response.json();
      setMemories(data.memories);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to fetch memories');
    } finally {
      setLoading(false);
    }
  };

  const fetchStats = async () => {
    try {
      const response = await fetch(`${apiBaseUrl}/api/Memory/stats`);
      if (!response.ok) {
        throw new Error(`Failed to fetch stats: ${response.statusText}`);
      }
      
      const data: MemoryStatsResponse = await response.json();
      setStats(data);
    } catch (err) {
      console.error('Failed to fetch stats:', err);
    }
  };

  const searchMemories = useCallback(async (query: string) => {
    if (!query.trim()) {
      fetchMemories();
      return;
    }

    try {
      setLoading(true);
      setError(null);
      
      const response = await fetch(`${apiBaseUrl}/api/Memory/search?query=${encodeURIComponent(query)}&maxResults=100`);
      if (!response.ok) {
        throw new Error(`Failed to search memories: ${response.statusText}`);
      }
      
      const data: MemoryListResponse = await response.json();
      setMemories(data.memories);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to search memories');
    } finally {
      setLoading(false);
    }
  }, []);

  const deleteMemory = async (key: string) => {
    try {
      const response = await fetch(`${apiBaseUrl}/api/Memory/${encodeURIComponent(key)}`, {
        method: 'DELETE',
      });
      
      if (!response.ok) {
        throw new Error(`Failed to delete memory: ${response.statusText}`);
      }
      
      setSuccess(`Memory "${key}" deleted successfully`);
      setSelectedMemories(prev => {
        const newSet = new Set(prev);
        newSet.delete(key);
        return newSet;
      });
      await fetchMemories();
      await fetchStats();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to delete memory');
    }
  };

  const deleteSelectedMemories = async () => {
    try {
      const promises = Array.from(selectedMemories).map(key => 
        fetch(`${apiBaseUrl}/api/Memory/${encodeURIComponent(key)}`, {
          method: 'DELETE',
        })
      );
      
      const results = await Promise.allSettled(promises);
      const failed = results.filter(result => result.status === 'rejected' || 
        (result.status === 'fulfilled' && !result.value.ok));
      
      if (failed.length > 0) {
        throw new Error(`Failed to delete ${failed.length} memories`);
      }
      
      setSuccess(`Successfully deleted ${selectedMemories.size} memories`);
      setSelectedMemories(new Set());
      await fetchMemories();
      await fetchStats();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to delete selected memories');
    }
  };

  const clearAllMemories = async () => {
    try {
      const response = await fetch(`${apiBaseUrl}/api/Memory/clear`, {
        method: 'DELETE',
      });
      
      if (!response.ok) {
        throw new Error(`Failed to clear all memories: ${response.statusText}`);
      }
      
      setSuccess('All memories cleared successfully');
      setSelectedMemories(new Set());
      await fetchMemories();
      await fetchStats();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to clear all memories');
    }
  };

  const handleSelectAll = (checked: boolean) => {
    if (checked) {
      setSelectedMemories(new Set(memories.map(m => m.key)));
    } else {
      setSelectedMemories(new Set());
    }
  };

  const handleSelectMemory = (key: string, checked: boolean) => {
    setSelectedMemories(prev => {
      const newSet = new Set(prev);
      if (checked) {
        newSet.add(key);
      } else {
        newSet.delete(key);
      }
      return newSet;
    });
  };

  const formatDate = (dateString: string) => {
    return new Date(dateString).toLocaleString();
  };

  const formatBytes = (bytes: number) => {
    if (bytes === 0) return '0 Bytes';
    const k = 1024;
    const sizes = ['Bytes', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
  };

  const filteredMemories = memories.filter(memory =>
    memory.key.toLowerCase().includes(searchQuery.toLowerCase()) ||
    memory.content.toLowerCase().includes(searchQuery.toLowerCase())
  );

  useEffect(() => {
    fetchMemories();
    fetchStats();
  }, []);

  useEffect(() => {
    const timeoutId = setTimeout(() => {
      if (searchQuery) {
        searchMemories(searchQuery);
      } else {
        fetchMemories();
      }
    }, 300);

    return () => clearTimeout(timeoutId);
  }, [searchQuery, searchMemories]);

  return (
    <Container maxWidth="xl">
      <Box sx={{ mb: 3 }}>
        <Typography variant="h4" component="h1" gutterBottom>
        🗃️ Memory Configuration
        </Typography>
        <Typography variant="body1" color="text.secondary">
          Manage stored memories and view memory statistics
        </Typography>
      </Box>

      {error && (
        <Alert severity="error" sx={{ mb: 2 }} onClose={() => setError(null)}>
          {error}
        </Alert>
      )}

      {success && (
        <Alert severity="success" sx={{ mb: 2 }} onClose={() => setSuccess(null)}>
          {success}
        </Alert>
      )}

      {/* Statistics Cards */}
      {stats && (
        <Grid container spacing={2} sx={{ mb: 3 }}>
          <Grid item xs={12} sm={6} md={3}>
            <Card>
              <CardContent>
                <Box sx={{ display: 'flex', alignItems: 'center' }}>
                  <MemoryIcon color="primary" sx={{ mr: 1 }} />
                  <Box>
                    <Typography variant="h6">{stats.totalMemories}</Typography>
                    <Typography variant="body2" color="text.secondary">
                      Total Memories
                    </Typography>
                  </Box>
                </Box>
              </CardContent>
            </Card>
          </Grid>
          <Grid item xs={12} sm={6} md={3}>
            <Card>
              <CardContent>
                <Box sx={{ display: 'flex', alignItems: 'center' }}>
                  <StorageIcon color="primary" sx={{ mr: 1 }} />
                  <Box>
                    <Typography variant="h6">{formatBytes(stats.totalSizeBytes)}</Typography>
                    <Typography variant="body2" color="text.secondary">
                      Total Size
                    </Typography>
                  </Box>
                </Box>
              </CardContent>
            </Card>
          </Grid>
          <Grid item xs={12} sm={6} md={3}>
            <Card>
              <CardContent>
                <Box sx={{ display: 'flex', alignItems: 'center' }}>
                  <TrendingUpIcon color="primary" sx={{ mr: 1 }} />
                  <Box>
                    <Typography variant="h6">{stats.memoriesWithEmbeddings}</Typography>
                    <Typography variant="body2" color="text.secondary">
                      With Embeddings
                    </Typography>
                  </Box>
                </Box>
              </CardContent>
            </Card>
          </Grid>
          <Grid item xs={12} sm={6} md={3}>
            <Card>
              <CardContent>
                <Box sx={{ display: 'flex', alignItems: 'center' }}>
                  <ScheduleIcon color="primary" sx={{ mr: 1 }} />
                  <Box>
                    <Typography variant="h6">{stats.recentlyAccessed}</Typography>
                    <Typography variant="body2" color="text.secondary">
                      Recently Accessed
                    </Typography>
                  </Box>
                </Box>
              </CardContent>
            </Card>
          </Grid>
        </Grid>
      )}

      {/* Controls */}
      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Stack direction="row" spacing={2} alignItems="center" sx={{ mb: 2 }}>
            <TextField
              placeholder="Search memories..."
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              InputProps={{
                startAdornment: (
                  <InputAdornment position="start">
                    <SearchIcon />
                  </InputAdornment>
                ),
                endAdornment: searchQuery && (
                  <InputAdornment position="end">
                    <IconButton onClick={() => setSearchQuery('')} size="small">
                      <ClearIcon />
                    </IconButton>
                  </InputAdornment>
                ),
              }}
              sx={{ flexGrow: 1 }}
            />
            <Button
              variant="outlined"
              startIcon={<RefreshIcon />}
              onClick={() => {
                fetchMemories();
                fetchStats();
              }}
              disabled={loading}
            >
              Refresh
            </Button>
          </Stack>

          <Stack direction="row" spacing={2} alignItems="center">
            <FormControlLabel
              control={
                <Checkbox
                  checked={selectedMemories.size === filteredMemories.length && filteredMemories.length > 0}
                  indeterminate={selectedMemories.size > 0 && selectedMemories.size < filteredMemories.length}
                  onChange={(e) => handleSelectAll(e.target.checked)}
                />
              }
              label={`Select All (${selectedMemories.size} selected)`}
            />
            
            {selectedMemories.size > 0 && (
              <>
                <Button
                  variant="outlined"
                  color="error"
                  startIcon={<DeleteIcon />}
                  onClick={() => setDeleteDialogOpen(true)}
                >
                  Delete Selected ({selectedMemories.size})
                </Button>
              </>
            )}
            
            <Button
              variant="outlined"
              color="error"
              startIcon={<ClearIcon />}
              onClick={() => setClearAllDialogOpen(true)}
              disabled={memories.length === 0}
            >
              Clear All
            </Button>
          </Stack>
        </CardContent>
      </Card>

      {/* Memories Table */}
      <Card>
        <CardContent>
          {loading ? (
            <Box sx={{ display: 'flex', justifyContent: 'center', p: 3 }}>
              <CircularProgress />
            </Box>
          ) : filteredMemories.length === 0 ? (
            <Box sx={{ textAlign: 'center', p: 3 }}>
              <Typography variant="h6" color="text.secondary">
                {searchQuery ? 'No memories found matching your search' : 'No memories stored'}
              </Typography>
            </Box>
          ) : (
            <TableContainer component={Paper} variant="outlined">
              <Table>
                <TableHead>
                  <TableRow>
                    <TableCell padding="checkbox">
                      <Checkbox
                        checked={selectedMemories.size === filteredMemories.length && filteredMemories.length > 0}
                        indeterminate={selectedMemories.size > 0 && selectedMemories.size < filteredMemories.length}
                        onChange={(e) => handleSelectAll(e.target.checked)}
                      />
                    </TableCell>
                    <TableCell>Key</TableCell>
                    <TableCell>Content</TableCell>
                    <TableCell>Created</TableCell>
                    <TableCell>Updated</TableCell>
                    <TableCell>Access Count</TableCell>
                    <TableCell>Last Accessed</TableCell>
                    <TableCell>Embedding</TableCell>
                    <TableCell>Actions</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {filteredMemories.map((memory) => (
                    <TableRow key={memory.key} hover>
                      <TableCell padding="checkbox">
                        <Checkbox
                          checked={selectedMemories.has(memory.key)}
                          onChange={(e) => handleSelectMemory(memory.key, e.target.checked)}
                        />
                      </TableCell>
                      <TableCell>
                        <Typography variant="body2" sx={{ fontFamily: 'monospace' }}>
                          {memory.key}
                        </Typography>
                      </TableCell>
                      <TableCell>
                        <Typography
                          variant="body2"
                          sx={{
                            maxWidth: 300,
                            overflow: 'hidden',
                            textOverflow: 'ellipsis',
                            whiteSpace: 'nowrap'
                          }}
                          title={memory.content}
                        >
                          {memory.content}
                        </Typography>
                      </TableCell>
                      <TableCell>
                        <Typography variant="body2">
                          {formatDate(memory.createdAt)}
                        </Typography>
                      </TableCell>
                      <TableCell>
                        <Typography variant="body2">
                          {formatDate(memory.updatedAt)}
                        </Typography>
                      </TableCell>
                      <TableCell>
                        <Chip
                          label={memory.accessCount}
                          size="small"
                          color={memory.accessCount > 0 ? 'primary' : 'default'}
                        />
                      </TableCell>
                      <TableCell>
                        <Typography variant="body2">
                          {memory.lastAccessedAt ? formatDate(memory.lastAccessedAt) : 'Never'}
                        </Typography>
                      </TableCell>
                      <TableCell>
                        <Chip
                          label={memory.hasEmbedding ? 'Yes' : 'No'}
                          size="small"
                          color={memory.hasEmbedding ? 'success' : 'default'}
                        />
                      </TableCell>
                      <TableCell>
                        <Tooltip title="Delete memory">
                          <IconButton
                            size="small"
                            color="error"
                            onClick={() => {
                              setMemoryToDelete(memory.key);
                              setDeleteDialogOpen(true);
                            }}
                          >
                            <DeleteIcon />
                          </IconButton>
                        </Tooltip>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>
          )}
        </CardContent>
      </Card>

      {/* Delete Confirmation Dialog */}
      <Dialog
        open={deleteDialogOpen}
        onClose={() => {
          setDeleteDialogOpen(false);
          setMemoryToDelete(null);
        }}
      >
        <DialogTitle>Confirm Delete</DialogTitle>
        <DialogContent>
          <DialogContentText>
            {memoryToDelete
              ? `Are you sure you want to delete the memory "${memoryToDelete}"? This action cannot be undone.`
              : `Are you sure you want to delete ${selectedMemories.size} selected memories? This action cannot be undone.`
            }
          </DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => {
            setDeleteDialogOpen(false);
            setMemoryToDelete(null);
          }}>
            Cancel
          </Button>
          <Button
            onClick={async () => {
              if (memoryToDelete) {
                await deleteMemory(memoryToDelete);
              } else {
                await deleteSelectedMemories();
              }
              setDeleteDialogOpen(false);
              setMemoryToDelete(null);
            }}
            color="error"
            autoFocus
          >
            Delete
          </Button>
        </DialogActions>
      </Dialog>

      {/* Clear All Confirmation Dialog */}
      <Dialog
        open={clearAllDialogOpen}
        onClose={() => setClearAllDialogOpen(false)}
      >
        <DialogTitle>Confirm Clear All</DialogTitle>
        <DialogContent>
          <DialogContentText>
            Are you sure you want to delete ALL memories? This action cannot be undone and will remove {memories.length} memories.
          </DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setClearAllDialogOpen(false)}>
            Cancel
          </Button>
          <Button
            onClick={async () => {
              await clearAllMemories();
              setClearAllDialogOpen(false);
            }}
            color="error"
            autoFocus
          >
            Clear All
          </Button>
        </DialogActions>
      </Dialog>
    </Container>
  );
};

export default MemoryConfig;
