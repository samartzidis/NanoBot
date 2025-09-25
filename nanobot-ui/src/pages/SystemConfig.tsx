import React, { useEffect, useState, useCallback } from 'react';
import { JsonForms } from '@jsonforms/react';
import { materialRenderers, materialCells } from '@jsonforms/material-renderers';
import { CssBaseline, Container, Button, Stack, Box, Dialog, DialogTitle, DialogContent, DialogActions, DialogContentText } from '@mui/material';
import { JsonSchema, UISchemaElement, JsonFormsRendererRegistryEntry, RankedTester } from '@jsonforms/core';
import { createAjv } from '@jsonforms/core';

import defaultUiSchema from '../systemUiSchema';
import { apiBaseUrl } from '../config';
import FileUploadDownloadRenderer from '../components/FileUploadDownloadRenderer';

const SystemConfig: React.FC = () => {
  const [schema, setSchema] = useState<JsonSchema | null>(null);
  const [uischema, setUiSchema] = useState<UISchemaElement | null>(null);
  const [data, setData] = useState<Record<string, unknown> | null>(null);
  const [isValid, setIsValid] = useState(true);
  const [openDialog, setOpenDialog] = useState(false);
  const [isJsonValid, setIsJsonValid] = useState(true);

  // Stable callback to prevent re-renders
  const handleJsonValidationChange = useCallback((isValid: boolean) => {
    setIsJsonValid(isValid);
  }, []);

  // Create stable renderer component
  const FileUploadDownloadRendererWrapper = useCallback((props: Record<string, unknown>) => {
    const uischema = props.uischema as Record<string, unknown>;
    const options = uischema?.options as Record<string, unknown>;
    const contentType = options?.contentType as 'json' | 'text' | undefined;
    
    const componentProps = {
      ...props,
      onJsonValidationChange: handleJsonValidationChange,
      contentType: contentType || 'text'
    };
    // @ts-expect-error - onJsonValidationChange and contentType are custom props not in ControlProps
    return <FileUploadDownloadRenderer {...componentProps} />;
  }, [handleJsonValidationChange]);

  // Custom renderers
  const customRenderers: JsonFormsRendererRegistryEntry[] = [
    ...materialRenderers,
    {
      tester: ((uischema: UISchemaElement) => {
        const options = (uischema as unknown as Record<string, unknown>)?.options as Record<string, unknown>;
        return options?.customRenderer === 'file-upload-download' ? 10 : -1;
      }) as RankedTester,
      renderer: FileUploadDownloadRendererWrapper,
    },
  ];  

  useEffect(() => {
    const fetchSchemaAndSettings = async () => {
      try {
        const schemaResponse = await fetch(`${apiBaseUrl}/api/Configuration/GetSchema`);
        const schemaData = await schemaResponse.json();

        const settingsResponse = await fetch(`${apiBaseUrl}/api/Configuration/GetSettings`);
        const appSettings = await settingsResponse.json();

        setSchema(schemaData);
        setUiSchema(defaultUiSchema);
        setData(appSettings);
      } catch (error) {
        console.error('Error fetching schema or settings:', error);
      }
    };

    fetchSchemaAndSettings();
  }, []);

  const saveSettings = async () => {
    try {
      await fetch(`${apiBaseUrl}/api/Configuration/UpdateSettings`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(data),
      });
      alert('Settings saved successfully!');
    } catch (error) {
      console.error('Error saving settings:', error);
      alert('Failed to save settings.');
    }
  };

  const handleDeleteClick = () => {
    setOpenDialog(true);
  };

  const handleConfirmDelete = async () => {
    setOpenDialog(false);
    try {
      const response = await fetch(`${apiBaseUrl}/api/Configuration/DeleteSettings`, {
        method: 'DELETE',
      });

      if (response.ok) {
        alert('Settings deleted successfully!');
        setData(null);
      } else {
        const errorText = await response.text();
        alert(`Failed to delete settings: ${errorText}`);
      }
    } catch (error) {
      console.error('Error deleting settings:', error);
      alert('Failed to delete settings.');
    }
  };

  const handleDefaultsAjv = createAjv({ useDefaults: true });

  if (!schema || !uischema || !data) {
    return (
      <Container sx={{ display: 'flex', justifyContent: 'center', alignItems: 'center', height: '80vh' }}>
        <img src="settings.gif" alt="Loading..." style={{ width: '64px', height: '64px' }} />
      </Container>
    );
  }

  return (
    <Container>
      <CssBaseline />
      <h1 className="mb-4">⚙️ System Configuration</h1>
      <JsonForms
        schema={schema}
        uischema={uischema}
        data={data}
        renderers={customRenderers}
        cells={materialCells}
        onChange={({ data, errors = [] }) => {
          setData(data);
          setIsValid(errors.length === 0);
        }}
        ajv={handleDefaultsAjv}
      />
      <Stack direction="row" spacing={2} sx={{ mt: 3, justifyContent: 'space-between', mb: 3 }}>
        <Box>
          <Button
            variant="contained"
            color="primary"
            onClick={saveSettings}
            disabled={!isValid || !isJsonValid}
          >
            Save
          </Button>
          <Button
            variant="outlined"
            color="secondary"
            onClick={handleDeleteClick}
            sx={{ ml: 2 }}
          >
            Reset to Defaults
          </Button>
        </Box>        
      </Stack>

      <Dialog
        open={openDialog}
        onClose={() => setOpenDialog(false)}
      >
        <DialogTitle>Confirm Reset</DialogTitle>
        <DialogContent>
          <DialogContentText>
            This will delete all custom user settings (including the configured agents) and go back to the system defaults. Are you sure?
          </DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setOpenDialog(false)}>Cancel</Button>
          <Button onClick={handleConfirmDelete} color="error" autoFocus>
            Delete
          </Button>
        </DialogActions>
      </Dialog>
    </Container>
  );
};

export default SystemConfig; 