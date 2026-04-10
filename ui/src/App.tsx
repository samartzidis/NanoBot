import React from 'react';
import { BrowserRouter, Routes, Route, Link, useLocation } from 'react-router-dom';
import { AppBar, Toolbar, Typography, Button, Container, Box } from '@mui/material';
import logo from './assets/logo.png';
import AgentsConfig from './pages/AgentsConfig';
import SystemConfig from './pages/SystemConfig';
import MemoryConfig from './pages/MemoryConfig';
import LogsConfig from './pages/LogsConfig';

const logoBackgroundStyle: React.CSSProperties = {
  position: 'fixed',
  top: 0,
  left: 0,
  right: 0,
  bottom: 0,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  pointerEvents: 'none',
  zIndex: 0,
};

const logoImageStyle: React.CSSProperties = {
  maxWidth: 'min(60vw, 480px)',
  height: 'auto',
  opacity: 0.06,
};

const AppContent: React.FC = () => {
  const location = useLocation();
  const isHome = location.pathname === '/';

  return (
    <>
      <Box sx={{ position: 'relative', zIndex: 1, minHeight: '100vh' }}>
        <AppBar position="static">
          <Toolbar>
            <Typography variant="h6" component="div" sx={{ flexGrow: 1 }}>
              NanoBot
            </Typography>
            <Button color="inherit" component={Link} to="/">
              Home
            </Button>
            <Button color="inherit" component={Link} to="/system">
              System
            </Button>
            <Button color="inherit" component={Link} to="/agents">
              Agents
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
            <Route path="/" element={
              <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', paddingTop: 24, gap: 24 }}>
                <img src={logo} alt="NanoBot logo" style={{ maxWidth: '100%', height: 'auto' }} />
              </div>
            } />
            <Route path="/system" element={<SystemConfig />} />
            <Route path="/agents" element={<AgentsConfig />} />            
            <Route path="/memory" element={<MemoryConfig />} />
            <Route path="/logs" element={<LogsConfig />} />
          </Routes>
        </Container>
      </Box>
      {!isHome && (
        <div style={logoBackgroundStyle}>
          <img src={logo} alt="" aria-hidden style={logoImageStyle} />
        </div>
      )}
    </>
  );
};

const App: React.FC = () => {
  return (
    <BrowserRouter>
      <AppContent />
    </BrowserRouter>
  );
};

export default App;
