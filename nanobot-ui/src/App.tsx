import React from 'react';
import { BrowserRouter, Routes, Route, Link } from 'react-router-dom';
import { AppBar, Toolbar, Typography, Button, Container } from '@mui/material';
import Config from './pages/Config';
import SystemConfig from './pages/SystemConfig';
import { apiBaseUrl } from './config';

const App: React.FC = () => {
  
  const openStreamingLogs = () => {
    const logsUrl = `${apiBaseUrl}/api/System/StreamLogs`;
    window.open(logsUrl, '_blank');
  };

  return (
    <BrowserRouter>
      <AppBar position="static">
        <Toolbar>
          <Typography variant="h6" component="div" sx={{ flexGrow: 1 }}>
            NanoBot
          </Typography>
          <Button color="inherit" component={Link} to="/">
            Home
          </Button>
          <Button color="inherit" component={Link} to="/config">
            Agents
          </Button>
          <Button color="inherit" component={Link} to="/system">
            System
          </Button>
          <Button color="inherit" onClick={openStreamingLogs}>
            Logs
          </Button>
        </Toolbar>
      </AppBar>

      <Container sx={{ mt: 3 }}>
        <Routes>
          <Route path="/" element={<div style={{ display: 'flex', justifyContent: 'center', height: '100vh' }}>Welcome to NanoBot.</div>} />
          <Route path="/config" element={<Config />} />
          <Route path="/system" element={<SystemConfig />} />
        </Routes>
      </Container>
    </BrowserRouter>
  );
};

export default App;
