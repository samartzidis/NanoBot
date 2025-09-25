import React, { useState, useRef, useEffect, useCallback } from 'react';
import { withJsonFormsControlProps } from '@jsonforms/react';
import { 
  Box, 
  Typography, 
  Button, 
  Stack, 
  Paper,
  Chip
} from '@mui/material';
import { 
  Upload, 
  Download, 
  CheckCircle, 
  Error as ErrorIcon,
  Cancel,
  Description
} from '@mui/icons-material';
import { ControlProps } from '@jsonforms/core';

interface FileUploadDownloadRendererProps extends ControlProps {
  onJsonValidationChange?: (isValid: boolean) => void;
  contentType?: 'json' | 'text';
}

const FileUploadDownloadRenderer: React.FC<FileUploadDownloadRendererProps> = (props) => {
  const { data, handleChange, path, label, schema, onJsonValidationChange, contentType = 'text' } = props;
  const [jsonError, setJsonError] = useState<string | null>(null);
  const [localValue, setLocalValue] = useState<string>('');
  const [originalValue, setOriginalValue] = useState<string>('');
  const [isValidJson, setIsValidJson] = useState<boolean>(true);
  const [uploadedFileInfo, setUploadedFileInfo] = useState<{name: string, size: number} | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const validationTimeoutRef = useRef<NodeJS.Timeout | null>(null);
  const localValueRef = useRef<string>('');

  // Extract DisplayName and Description from schema
  // Prioritize explicit label from UI schema over schema title (DisplayName)
  const displayName = label || schema?.title;
  const description = schema?.description;

  // Initialize local value from data (only on initial mount)
  useEffect(() => {
    let initialValue = '';
    if (data === null || data === undefined) {
      initialValue = '';
    } else if (typeof data === 'string') {
      initialValue = data;
    } else {
      initialValue = JSON.stringify(data, null, 2);
    }
    setLocalValue(initialValue);
    setOriginalValue(initialValue);
    localValueRef.current = initialValue;
  }, []); // Only run on mount, not on data changes - we handle data updates through handleChange

  // Keep ref in sync with localValue
  useEffect(() => {
    localValueRef.current = localValue;
  }, [localValue]);

  // Stable validation function
  const validateContent = useCallback((value: string) => {
    // Empty content is considered valid
    if (!value.trim()) {
      setJsonError(null);
      setIsValidJson(true);
      onJsonValidationChange?.(true);
      return;
    }
    
    // Only validate JSON if contentType is 'json'
    if (contentType === 'json') {
      try {
        JSON.parse(value);
        setJsonError(null);
        setIsValidJson(true);
        onJsonValidationChange?.(true);
      } catch (error) {
        const errorMessage = error instanceof Error ? error.message : 'Invalid JSON';
        setJsonError(errorMessage);
        setIsValidJson(false);
        onJsonValidationChange?.(false);
      }
    } else {
      // For text content, always consider it valid
      setJsonError(null);
      setIsValidJson(true);
      onJsonValidationChange?.(true);
    }
  }, [onJsonValidationChange, contentType]);

  // Validate content when local value changes
  useEffect(() => {
    if (validationTimeoutRef.current) {
      clearTimeout(validationTimeoutRef.current);
    }

    validationTimeoutRef.current = setTimeout(() => {
      validateContent(localValue);
      handleChange(path, localValue);
    }, 300);

    return () => {
      if (validationTimeoutRef.current) {
        clearTimeout(validationTimeoutRef.current);
      }
    };
  }, [localValue, validateContent, handleChange, path]);

  const handleFileUpload = (event: React.ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    
    // Clear the input value so the same file can be selected again
    event.target.value = '';
    
    if (!file) return;

    // Check file extension based on content type
    const expectedExtension = contentType === 'json' ? '.json' : '.txt';
    if (!file.name.toLowerCase().endsWith(expectedExtension)) {
      setJsonError(`Please select a ${contentType.toUpperCase()} file (${expectedExtension})`);
      setIsValidJson(false);
      onJsonValidationChange?.(false);
      return;
    }

    const reader = new FileReader();
    reader.onload = (e) => {
      const content = e.target?.result as string;
      if (content) {
        // Store current value as original before uploading
        setOriginalValue(localValueRef.current);
        setLocalValue(content);
        setUploadedFileInfo({ name: file.name, size: file.size });
        validateContent(content);
      }
    };
    reader.onerror = () => {
      setJsonError('Failed to read file');
      setIsValidJson(false);
      onJsonValidationChange?.(false);
    };
    reader.readAsText(file);
  };

  const handleDownload = () => {
    // Always download the server data, not the local uploaded data
    let serverData = '';
    if (data === null || data === undefined) {
      serverData = '';
    } else if (typeof data === 'string') {
      serverData = data;
    } else {
      serverData = JSON.stringify(data, null, 2);
    }

    if (!serverData.trim()) {
      setJsonError('No server data to download');
      return;
    }

    const mimeType = contentType === 'json' ? 'application/json' : 'text/plain';
    const fileExtension = contentType === 'json' ? '.json' : '.txt';
    const blob = new Blob([serverData], { type: mimeType });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `data${fileExtension}`;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
  };

  const handleUploadClick = () => {
    fileInputRef.current?.click();
  };

  const formatFileSize = (bytes: number): string => {
    if (bytes === 0) return '0 Bytes';
    const k = 1024;
    const sizes = ['Bytes', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
  };

  const getCurrentDataSize = (): number => {
    return new Blob([localValue]).size;
  };

  const handleClearData = () => {
    // Clear any pending validation timeout
    if (validationTimeoutRef.current) {
      clearTimeout(validationTimeoutRef.current);
      validationTimeoutRef.current = null;
    }
    
    // Restore original value and clear uploaded file info
    setLocalValue(originalValue);
    setUploadedFileInfo(null);
    setJsonError(null);
    setIsValidJson(true);
    onJsonValidationChange?.(true);
    handleChange(path, originalValue);
  };

  // Cleanup timeout on unmount
  useEffect(() => {
    return () => {
      if (validationTimeoutRef.current) {
        clearTimeout(validationTimeoutRef.current);
      }
    };
  }, []);


  return (
    <Box sx={{ width: '100%' }}>
      {/* Label */}
      <Typography 
        variant="body2" 
        sx={{ 
          mb: 1, 
          fontWeight: 500,
          color: 'text.secondary'
        }}
      >
        {displayName}
      </Typography>
      
      {/* Description */}
      {description && (
        <Typography 
          variant="caption" 
          sx={{ 
            mb: 2, 
            display: 'block',
            color: 'text.secondary',
            fontStyle: 'italic'
          }}
        >
          {description}
        </Typography>
      )}
      

      {/* File upload/download controls */}
      <Paper 
        elevation={1} 
        sx={{ 
          p: 3, 
          border: '1px solid #e0e0e0',
          borderRadius: 2,
          backgroundColor: '#fafafa'
        }}
      >
        <Stack spacing={3}>
          {/* Upload section */}
          <Box>
            <Typography variant="subtitle2" sx={{ mb: 1, fontWeight: 600 }}>
              Upload Data File
            </Typography>
            <Stack direction="row" spacing={2} alignItems="center">
              <Button
                variant="outlined"
                startIcon={<Upload />}
                onClick={handleUploadClick}
                sx={{ minWidth: 140 }}
              >
                Choose File
              </Button>
              {localValue.trim() && (
                <Button
                  variant="outlined"
                  startIcon={<Cancel />}
                  onClick={handleClearData}
                  color="error"
                  sx={{ minWidth: 140 }}
                >
                  Cancel
                </Button>
              )}
              <Typography variant="body2" color="text.secondary">
                Select a {contentType === 'json' ? '.json' : '.txt'} data file to upload
              </Typography>
            </Stack>
            <input
              ref={fileInputRef}
              type="file"
              accept={contentType === 'json' ? '.json' : '.txt'}
              onChange={handleFileUpload}
              style={{ display: 'none' }}
            />
          </Box>

          {/* Uploaded file info */}
          {uploadedFileInfo && (
            <Box>
              <Typography variant="subtitle2" sx={{ mb: 1, fontWeight: 600 }}>
                Uploaded File
              </Typography>
              <Paper 
                elevation={0} 
                sx={{ 
                  p: 2, 
                  backgroundColor: '#f0f8ff',
                  border: '1px solid #b3d9ff',
                  borderRadius: 1
                }}
              >
                <Stack direction="row" spacing={2} alignItems="center">
                  <Chip
                    icon={<Description />}
                    label={uploadedFileInfo.name}
                    variant="outlined"
                    color="primary"
                  />
                </Stack>
              </Paper>
            </Box>
          )}

          {/* Validation status */}
          {localValue.trim() && contentType === 'json' && (
            <Box>
              <Typography variant="subtitle2" sx={{ mb: 1, fontWeight: 600 }}>
                Validation Status
              </Typography>
              <Stack direction="row" spacing={2} alignItems="center">
                <Chip
                  icon={isValidJson ? <CheckCircle /> : <ErrorIcon />}
                  label={isValidJson ? 'Valid JSON' : 'Invalid JSON'}
                  color={isValidJson ? 'success' : 'error'}
                  variant="filled"
                  size="small"
                />
                {!isValidJson && jsonError && (
                  <Typography variant="body2" color="error" sx={{ fontWeight: 500 }}>
                    {jsonError}
                  </Typography>
                )}
              </Stack>
            </Box>
          )}

          {/* Current data size */}
          {localValue.trim() && (
            <Box>
              <Typography variant="subtitle2" sx={{ mb: 1, fontWeight: 600 }}>
                Data
              </Typography>
              <Paper 
                elevation={0} 
                sx={{ 
                  p: 2, 
                  backgroundColor: '#f5f5f5',
                  border: '1px solid #e0e0e0',
                  borderRadius: 1
                }}
              >
                <Typography variant="body2" color="text.secondary">
                  Size: {formatFileSize(getCurrentDataSize())}
                </Typography>
              </Paper>
            </Box>
          )}

          {/* Download section */}
          <Box>
            <Typography variant="subtitle2" sx={{ mb: 1, fontWeight: 600 }}>
              Download Data File
            </Typography>
            <Stack direction="row" spacing={2} alignItems="center">
              <Button
                variant="contained"
                startIcon={<Download />}
                onClick={handleDownload}
                disabled={!data || (typeof data === 'string' && !data.trim())}
                sx={{ minWidth: 140 }}
              >
                Download
              </Button>
              <Typography variant="body2" color="text.secondary">
                {data && (typeof data !== 'string' || data.trim())
                  ? `Download current ${contentType === 'json' ? '.json' : '.txt'} data file`
                  : 'No server data to download'
                }
              </Typography>
            </Stack>
          </Box>

        </Stack>
      </Paper>
    </Box>
  );
};

export default withJsonFormsControlProps(FileUploadDownloadRenderer);
