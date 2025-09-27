import React from 'react';
import { BrowserRouter, Routes, Route, Link } from 'react-router-dom';
import { AppBar, Toolbar, Typography, Button, Container } from '@mui/material';
import AgentsConfig from './pages/AgentsConfig';
import SystemConfig from './pages/SystemConfig';
import MemoryConfig from './pages/MemoryConfig';
import LogsConfig from './pages/LogsConfig';
import { apiBaseUrl } from './config';

const App: React.FC = () => {
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
          <Button color="inherit" component={Link} to="/memory">
            Memory
          </Button>
          <Button color="inherit" component={Link} to="/logs">
            Logs
          </Button>
        </Toolbar>
      </AppBar>

      <Container sx={{ mt: 3 }}>
        <Routes>
          <Route path="/" element={<div style={{ display: 'flex', justifyContent: 'center', height: '100vh' }}>Welcome to NanoBot.</div>} />
          <Route path="/config" element={<AgentsConfig />} />
          <Route path="/system" element={<SystemConfig />} />
          <Route path="/memory" element={<MemoryConfig />} />
          <Route path="/logs" element={<LogsConfig />} />
        </Routes>
      </Container>
    </BrowserRouter>
  );
};

export default App;
