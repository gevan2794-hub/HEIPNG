const { app, BrowserWindow, ipcMain, dialog } = require('electron');
const path = require('path');
const fs = require('fs');
const sharp = require('sharp');

let mainWindow;

function createWindow() {
  mainWindow = new BrowserWindow({
    width: 800,
    height: 650,
    minWidth: 600,
    minHeight: 500,
    frame: false,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
    },
    icon: path.join(__dirname, 'icon.ico'),
    title: 'HEIPNG',
    backgroundColor: '#313338',
  });

  mainWindow.loadFile('index.html');
}

app.whenReady().then(createWindow);

app.on('window-all-closed', () => {
  app.quit();
});

// Window controls
ipcMain.on('window-minimize', () => mainWindow.minimize());
ipcMain.on('window-maximize', () => {
  if (mainWindow.isMaximized()) {
    mainWindow.unmaximize();
  } else {
    mainWindow.maximize();
  }
});
ipcMain.on('window-close', () => mainWindow.close());

// Select HEIC files
ipcMain.handle('select-files', async () => {
  const result = await dialog.showOpenDialog(mainWindow, {
    title: 'Select HEIC Images',
    filters: [{ name: 'HEIC Images', extensions: ['heic', 'heif', 'HEIC', 'HEIF'] }],
    properties: ['openFile', 'multiSelections'],
  });
  return result.canceled ? [] : result.filePaths;
});

// Select output folder
ipcMain.handle('select-output-folder', async () => {
  const result = await dialog.showOpenDialog(mainWindow, {
    title: 'Select Output Folder',
    properties: ['openDirectory', 'createDirectory'],
  });
  return result.canceled ? null : result.filePaths[0];
});

// Convert a single file
ipcMain.handle('convert-file', async (_event, { filePath, outputDir, format, quality }) => {
  try {
    const ext = format === 'png' ? '.png' : '.jpg';
    const baseName = path.basename(filePath, path.extname(filePath));
    const outputPath = path.join(outputDir, baseName + ext);

    let pipeline = sharp(filePath, { failOn: 'none' });

    if (format === 'png') {
      pipeline = pipeline.png({ compressionLevel: 6 });
    } else {
      pipeline = pipeline.jpeg({ quality: quality || 90 });
    }

    await pipeline.toFile(outputPath);

    return { success: true, outputPath };
  } catch (err) {
    try {
      const convert = require('heic-convert');
      const inputBuffer = await fs.promises.readFile(filePath);
      const outputBuffer = await convert({
        buffer: inputBuffer,
        format: format === 'png' ? 'PNG' : 'JPEG',
        quality: (quality || 90) / 100,
      });

      const ext = format === 'png' ? '.png' : '.jpg';
      const baseName = path.basename(filePath, path.extname(filePath));
      const outputPath = path.join(outputDir, baseName + ext);

      await fs.promises.writeFile(outputPath, Buffer.from(outputBuffer));
      return { success: true, outputPath };
    } catch (fallbackErr) {
      return { success: false, error: fallbackErr.message };
    }
  }
});
