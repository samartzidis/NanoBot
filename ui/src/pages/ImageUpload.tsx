import React, { useEffect, useRef, useState } from 'react';
import { Alert, Box, Button, CircularProgress, Stack, Typography } from '@mui/material';
import { apiBaseUrl } from '../config';

const MAX_DIMENSION = 1024;
const JPEG_QUALITY = 0.8;

// Downscale to MAX_DIMENSION and re-encode as JPEG. Decoding through createImageBitmap applies the
// EXIF orientation, so phone photos come out upright.
async function downscale(file: File): Promise<Blob> {
  const bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' });
  const scale = Math.min(1, MAX_DIMENSION / Math.max(bitmap.width, bitmap.height));
  const canvas = document.createElement('canvas');
  canvas.width = Math.round(bitmap.width * scale);
  canvas.height = Math.round(bitmap.height * scale);
  canvas.getContext('2d')!.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
  bitmap.close();

  return new Promise<Blob>((resolve, reject) =>
    canvas.toBlob(b => (b ? resolve(b) : reject(new Error('Could not encode image.'))), 'image/jpeg', JPEG_QUALITY));
}

const ImageUpload: React.FC = () => {
  const cameraInput = useRef<HTMLInputElement>(null);
  const libraryInput = useRef<HTMLInputElement>(null);
  const [image, setImage] = useState<Blob | null>(null);
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [sent, setSent] = useState(false);

  useEffect(() => {
    if (!image) {
      setPreviewUrl(null);
      return;
    }
    const url = URL.createObjectURL(image);
    setPreviewUrl(url);
    return () => URL.revokeObjectURL(url);
  }, [image]);

  const onFileSelected = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    e.target.value = ''; // allow selecting the same file again
    if (!file) return;

    setError(null);
    setSent(false);
    setBusy(true);
    try {
      setImage(await downscale(file));
    } catch (err) {
      setImage(null);
      setError(err instanceof Error ? err.message : 'Could not read the image.');
    } finally {
      setBusy(false);
    }
  };

  const send = async () => {
    if (!image) return;

    setError(null);
    setBusy(true);
    try {
      const form = new FormData();
      form.append('file', image, 'picture.jpg');
      const response = await fetch(`${apiBaseUrl}/api/Conversation/Image`, { method: 'POST', body: form });
      if (!response.ok) {
        const body = await response.json().catch(() => null);
        throw new Error(body?.error ?? `Upload failed (${response.status}).`);
      }
      setSent(true);
      setImage(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Upload failed.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <Stack spacing={2} sx={{ maxWidth: 480, mx: 'auto', alignItems: 'stretch' }}>
      <Typography variant="h5">Send a picture</Typography>
      <Typography variant="body2" color="text.secondary">
        Take or choose a picture, send it, then wake the assistant and ask about it.
      </Typography>

      <input ref={cameraInput} type="file" accept="image/*" capture="environment" hidden onChange={onFileSelected} />
      <input ref={libraryInput} type="file" accept="image/*" hidden onChange={onFileSelected} />

      <Button variant="contained" size="large" disabled={busy} onClick={() => cameraInput.current?.click()}>
        Take picture
      </Button>
      <Button variant="outlined" disabled={busy} onClick={() => libraryInput.current?.click()}>
        Choose from library
      </Button>

      {previewUrl && (
        <Box component="img" src={previewUrl} alt="Preview" sx={{ width: '100%', borderRadius: 1 }} />
      )}

      {image && (
        <Button variant="contained" color="success" size="large" disabled={busy} onClick={send}>
          Send to assistant
        </Button>
      )}

      {busy && <CircularProgress sx={{ alignSelf: 'center' }} />}
      {sent && <Alert severity="success">Picture sent. Now ask the assistant about it.</Alert>}
      {error && <Alert severity="error">{error}</Alert>}
    </Stack>
  );
};

export default ImageUpload;
