//export const apiBaseUrl = window.location.origin; 
//export const apiBaseUrl = "http://localhost:8080";

export const apiBaseUrl = process.env.NODE_ENV === 'development'
  ? 'http://localhost:8080'
  : window.location.origin;
