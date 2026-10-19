// 通过 playwright-cli run-code --filename 使用；仅保留脱敏请求元数据。
async page => {
  await page.addInitScript(() => { window.perfJourney = { requests: [], sockets: [] }; });
  await page.evaluate(() => { window.perfJourney = { requests: [], sockets: [] }; });
  page.on('response', async response => {
    const request = response.request();
    const url = new URL(response.url());
    if (!url.pathname.startsWith('/api/') && !url.pathname.startsWith('/hubs/')) return;
    await response.finished().catch(() => {});
    const timing = request.timing();
    const entry = {
      method: request.method(), path: url.pathname,
      status: response.status(), utc: new Date(timing.startTime).toISOString(),
      durationMs: timing.responseEnd,
    };
    await page.evaluate(value => window.perfJourney.requests.push(value), entry);
  });
  page.on('websocket', socket => {
    const path = new URL(socket.url()).pathname;
    void page.evaluate(value => window.perfJourney.sockets.push(value), { path, event: 'connected' });
    socket.on('framereceived', frame => {
      if (typeof frame.payload !== 'string') return;
      void page.evaluate(value => window.perfJourney.sockets.push(value), { path, event: 'frame_received', bytes: frame.payload.length });
    });
  });
}
