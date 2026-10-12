import * as THREE from 'three'

/** A translucent moving ribbon, without a projectile head or impact rings. */
export function createSpectatorRibbon(start: THREE.Vector3, end: THREE.Vector3, color: THREE.Color) {
  const distance = start.distanceTo(end)
  const middle = start.clone().lerp(end, .5)
  middle.y += Math.min(4, distance * .14)
  const curve = new THREE.QuadraticBezierCurve3(start, middle, end)
  const positions: number[] = [], uvs: number[] = [], indices: number[] = []
  const segments = 72
  for (let i = 0; i <= segments; i++) {
    const t = i / segments
    const point = curve.getPoint(t)
    const tangent = curve.getTangent(t)
    const side = new THREE.Vector3(-tangent.z, .15, tangent.x).normalize()
    for (const sign of [-1, 1]) {
      const p = point.clone().addScaledVector(side, sign * .12)
      positions.push(p.x, p.y, p.z)
      uvs.push(t, sign)
    }
    if (i < segments) { const n = i * 2; indices.push(n, n + 1, n + 2, n + 1, n + 3, n + 2) }
  }
  const geometry = new THREE.BufferGeometry()
  geometry.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3))
  geometry.setAttribute('uv', new THREE.Float32BufferAttribute(uvs, 2))
  geometry.setIndex(indices)
  const material = new THREE.ShaderMaterial({
    transparent: true, depthWrite: false, side: THREE.DoubleSide, blending: THREE.AdditiveBlending,
    uniforms: { phase: { value: 0 }, tint: { value: color } },
    vertexShader: 'varying vec2 vUv; void main(){vUv=uv;gl_Position=projectionMatrix*modelViewMatrix*vec4(position,1.0);}',
    fragmentShader: `uniform float phase; uniform vec3 tint; varying vec2 vUv;
      void main(){
        float head=phase*1.55;
        float tail=smoothstep(head-.58,head-.06,vUv.x);
        float front=1.-smoothstep(head-.045,head,vUv.x);
        float taper=.55+.3*clamp((vUv.x-head+.58)/.58,0.,1.);
        float edge=1.-smoothstep(.12,max(.14,taper),abs(vUv.y));
        float fade=1.-smoothstep(.70,1.,phase);
        gl_FragColor=vec4(tint,tail*front*edge*fade*.85);
      }`,
  })
  return new THREE.Mesh(geometry, material)
}
