async (page) => {
  const requests = [];
  const capture = async request => {
    const path = new URL(request.url()).pathname;
    if (!path.startsWith('/api/') && !path.startsWith('/hubs/')) return;
    const response = await request.response();
    requests.push({ method: request.method(), path, status: response?.status(), durationMs: request.timing().responseEnd });
  };
  page.on('requestfinished', capture);
  try {
    await page.getByRole('link', { name: '返回比赛', exact: true }).click();
    await page.getByRole('textbox', { name: 'Flag', exact: true }).waitFor();
    await page.getByRole('link', { name: '记分板', exact: true }).click();
    await page.getByText('最近更新时间：', { exact: false }).waitFor();
    await page.getByRole('link', { name: '返回比赛', exact: true }).click();
    await page.getByRole('textbox', { name: 'Flag', exact: true }).waitFor();
    return { finalUrl: page.url(), requests };
  } finally {
    page.off('requestfinished', capture);
  }
}
