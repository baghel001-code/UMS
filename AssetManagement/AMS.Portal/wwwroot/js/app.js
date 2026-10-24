// Small browser helpers used from Blazor via IJSRuntime.
window.ams = {
  // download text (CSV export) as a file
  download: (filename, contentType, text) => {
    const a = document.createElement('a');
    a.href = URL.createObjectURL(new Blob([text], { type: contentType }));
    a.download = filename;
    a.click();
    URL.revokeObjectURL(a.href);
  },
  print: () => window.print(),
};
