import React, { useEffect, useState } from 'react';
import { JsonForms } from '@jsonforms/react';
import { materialRenderers, materialCells } from '@jsonforms/material-renderers';
import { CssBaseline, Container, Button, Stack, Box } from '@mui/material';
import { JsonSchema, UISchemaElement } from '@jsonforms/core';
import { createAjv } from '@jsonforms/core';

import mainUiSchema from '../mainUiSchema'; 
import { apiBaseUrl } from '../config';

const Config: React.FC = () => {
  const [schema, setSchema] = useState<JsonSchema | null>(null);
  const [uischema, setUiSchema] = useState<UISchemaElement | null>(null);
  const [data, setData] = useState<any>(null);
  const [isValid, setIsValid] = useState(true); // Track form validity

  useEffect(() => {
    const fetchSchemaAndSettings = async () => {
      try {
        const schemaResponse = await fetch(`${apiBaseUrl}/api/Configuration/GetSchema`);
        const schemaData = await schemaResponse.json();

        const settingsResponse = await fetch(`${apiBaseUrl}/api/Configuration/GetSettings`);
        const appSettings = await settingsResponse.json();

        setSchema(schemaData);
        setUiSchema(mainUiSchema);
        setData(appSettings);
      } catch (error) {
        console.error('Error fetching schema or settings:', error);
      }
    };

    fetchSchemaAndSettings();
  }, [apiBaseUrl]);

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
      <h1 className="mb-4">NanoBot Agents Configuration</h1>
      <JsonForms
        schema={schema}
        uischema={uischema}
        data={data}
        renderers={materialRenderers}
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
            disabled={!isValid} // Disable button if form is invalid
          >
            Save
          </Button>          
        </Box>
      </Stack>
    </Container>
  );
};

export default Config; 