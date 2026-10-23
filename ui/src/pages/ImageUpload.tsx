import React, { useEffect, useRef, useState } from 'react';
import { Alert, Box, Button, CircularProgress, Stack, Typography } from '@mui/material';
import ReactCrop, { type Crop, type PixelCrop } from 'react-image-crop';
import 'react-image-crop/dist/ReactCrop.css';
import { apiBaseUrl } from '../config';

const MAX_DIMENSION = 1024;
const JPEG_QUALITY = 0.8;
const MIN_CROP_PX = 10;

// Draw the cropped region of the rendered <img> onto a canvas, downscaled to MAX_DIMENSION, and
// re-encode as JPEG. Modern browsers auto-rotate <img> per EXIF, so naturalWidth/naturalHeight and
// the crop (reported in rendered-image pixels) already agree on orientation.
function getCroppedBlob(image: HTMLImageElement, crop: PixelCrop): Promise<Blob> {
  const scaleX = image.naturalWidth / image.width;
  const scaleY = image.naturalHeight / image.height;
  const cropWidth = crop.width * scaleX;
  const cropHeight = crop.height * scaleY;
  const scale = Math.min(1, MAX_DIMENSION / Math.max(cropWidth, cropHeight));

  const canvas = document.createElement('canvas');
  canvas.width = Math.round(cropWidth * scale);
  canvas.height = Math.round(cropHeight * scale);
  canvas
    .getContext('2d')!
    .drawImage(image, crop.x * scaleX, crop.y * scaleY, cropWidth, cropHeight, 0, 0, canvas.width, canvas.height);

  return new Promise<Blob>((resolve, reject) =>
    canvas.toBlob(b => (b ? resolve(b) : reject(new Error('Could not encode image.'))), 'image/jpeg', JPEG_QUALITY));
}

const ImageUpload: React.FC = () => {
  const cameraInput = useRef<HTMLInputElement>(null);
  const libraryInput = useRef<HTMLInputElement>(null);
  const imgRef = useRef<HTMLImageElement>(null);
  const [sourceFile, setSourceFile] = useState<File | null>(null);
  const [sourceUrl, setSourceUrl] = useState<string | null>(null);
  const [crop, setCrop] = useState<Crop>();
  const [completedCrop, setCompletedCrop] = useState<PixelCrop>();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [sent, setSent] = useState(false);

  useEffect(() => {
    if (!sourceFile) {
      setSourceUrl(null);
      return;
    }
    const url = URL.createObjectURL(sourceFile);
    setSourceUrl(url);
    return () => URL.revokeObjectURL(url);
  }, [sourceFile]);

  const onFileSelected = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    e.target.value = ''; // allow selecting the same file again
    if (!file) return;

    setError(null);
    setSent(false);
    setCrop(undefined);
    setCompletedCrop(undefined);
    setSourceFile(file);
  };

  const onImageLoad = (e: React.SyntheticEvent<HTMLImageElement>) => {
    const { width, height } = e.currentTarget;
    setCrop({ unit: '%', x: 0, y: 0, width: 100, height: 100 });
    setCompletedCrop({ unit: 'px', x: 0, y: 0, width, height });
  };

  const send = async () => {
    const image = imgRef.current;
    if (!image || !completedCrop || !completedCrop.width || !completedCrop.height) return;

    setError(null);
    setBusy(true);
    try {
      const blob = await getCroppedBlob(image, completedCrop);
      const form = new FormData();
      form.append('file', blob, 'picture.jpg');
      const response = await fetch(`${apiBaseUrl}/api/Conversation/Image`, { method: 'POST', body: form });
      if (!response.ok) {
        const body = await response.json().catch(() => null);
        throw new Error(body?.error ?? `Upload failed (${response.status}).`);
      }
      setSent(true);
      setSourceFile(null);
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
        Take or choose a picture, then drag the crop handles to send only part of it.
      </Typography>

      <input ref={cameraInput} type="file" accept="image/*" capture="environment" hidden onChange={onFileSelected} />
      <input ref={libraryInput} type="file" accept="image/*" hidden onChange={onFileSelected} />

      <Button variant="contained" size="large" disabled={busy} onClick={() => cameraInput.current?.click()}>
        Take picture
      </Button>
      <Button variant="outlined" disabled={busy} onClick={() => libraryInput.current?.click()}>
        Choose from library
      </Button>

      {sourceUrl && (
        <Button variant="contained" color="success" size="large" disabled={busy} onClick={send}>
          Send
        </Button>
      )}

      {sourceUrl && (
        <Box sx={{ width: '100%' }}>
          <ReactCrop
            crop={crop}
            onChange={(_, percentCrop) => setCrop(percentCrop)}
            onComplete={c => setCompletedCrop(c)}
            minWidth={MIN_CROP_PX}
            minHeight={MIN_CROP_PX}
          >
            <img ref={imgRef} src={sourceUrl} alt="Selected" onLoad={onImageLoad} style={{ width: '100%' }} />
          </ReactCrop>
        </Box>
      )}

      {busy && <CircularProgress sx={{ alignSelf: 'center' }} />}
      {sent && <Alert severity="success">Picture sent.</Alert>}
      {error && <Alert severity="error">{error}</Alert>}
    </Stack>
  );
};

export default ImageUpload;
