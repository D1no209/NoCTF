/** Execute a Nuxt controller with explicit platform/API dependencies in behavior tests. */
export async function featureController<T, TArgs extends unknown[] = []>(path: string, name: string, dependencies: Record<string, unknown>): Promise<(...args: TArgs) => T> {
  const source = await Bun.file(new URL(`../../app/${path}`, import.meta.url)).text()
  const code = new Bun.Transpiler({ loader: 'ts' }).transformSync(source)
    .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '')
    .replace(/export function /g, 'function ')
  return new Function('dependencies', `const { ${Object.keys(dependencies).join(', ')} } = dependencies; ${code}; return ${name}`)(dependencies)
}
