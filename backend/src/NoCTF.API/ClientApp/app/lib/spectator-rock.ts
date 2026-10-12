import * as THREE from 'three'

/** Object-space rock mapping keeps cliff faces detailed instead of stretching
 * a top-down texture over them. One shared tile and no extra terrain draw calls. */
export function createSpectatorRock(texture: THREE.Texture, tint: THREE.Color, dust: THREE.Color, ready: { value: number }) {
  const material = new THREE.MeshStandardMaterial({
    color: tint, roughness: .96, metalness: 0, vertexColors: true, flatShading: true, side: THREE.DoubleSide,
    polygonOffset: true, polygonOffsetFactor: 1, polygonOffsetUnits: 1,
  })
  material.customProgramCacheKey = () => 'spectator-eroded-mesa-v2'
  material.onBeforeCompile = shader => {
    shader.uniforms.rockTexture = { value: texture }
    shader.uniforms.rockReady = ready
    shader.uniforms.rockDust = { value: dust }
    shader.vertexShader = `attribute float mesaMask; varying float vMesaMask; varying vec3 vRockPosition; varying vec3 vRockNormal;\n${shader.vertexShader}`
      .replace('#include <begin_vertex>', '#include <begin_vertex>\nvRockPosition = position; vRockNormal = normal; vMesaMask = mesaMask;')
    shader.fragmentShader = `
      uniform sampler2D rockTexture;
      uniform float rockReady;
      uniform vec3 rockDust;
      varying float vMesaMask;
      varying vec3 vRockPosition;
      varying vec3 vRockNormal;
      float stoneHash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
      float stoneNoise(vec2 p) {
        vec2 i = floor(p), f = fract(p); f = f*f*(3.0-2.0*f);
        return mix(mix(stoneHash(i), stoneHash(i+vec2(1.,0.)), f.x),
                   mix(stoneHash(i+vec2(0.,1.)), stoneHash(i+vec2(1.,1.)), f.x), f.y);
      }
      vec3 rockProjection(vec3 p, vec3 weights) {
        return texture2D(rockTexture, p.zy).rgb * weights.x
             + texture2D(rockTexture, p.xz).rgb * weights.y
             + texture2D(rockTexture, p.xy).rgb * weights.z;
      }
      vec3 rockSurfaceNormal(vec3 p, vec3 n, float height, float facing) {
        vec3 dx = dFdx(p), dy = dFdy(p);
        vec3 rx = cross(dy, n), ry = cross(n, dx);
        float determinant = dot(dx, rx) * facing;
        vec3 gradient = sign(determinant) * (dFdx(height) * rx + dFdy(height) * ry);
        return normalize(max(abs(determinant), .000001) * n - gradient);
      }
    ${shader.fragmentShader}`
      .replace('#include <map_fragment>', `
        #include <map_fragment>
        vec3 rockWeights = pow(abs(normalize(vRockNormal)), vec3(5.0));
        rockWeights /= max(dot(rockWeights, vec3(1.0)), .001);
        vec3 scan = rockProjection(vRockPosition * .24, rockWeights);
        float grain = dot(scan, vec3(.2126,.7152,.0722));
        float rockSlope = 1.0 - abs(normalize(vRockNormal).y);
        float cliff = smoothstep(.18, .58, rockSlope);
        float broad = stoneNoise(vRockPosition.xz * .45);
        // Long erosion runnels are coherent down the wall, not stone-wall tiles.
        float runnels = stoneNoise(vRockPosition.xz * 4.8 + vec2(vRockPosition.y * .06));
        float fissure = smoothstep(.38, .56, runnels);
        float layers = .5 + .5*sin(vRockPosition.y*43.0 + broad*1.4);
        vec3 ledge = rockDust * (1.45 + broad*.4);
        vec3 wall = rockDust * (.68 + fissure*.38) * (.91 + layers*.09);
        vec3 rockAlbedo = mix(ledge, wall, cliff);
        rockAlbedo *= .43 + .57*smoothstep(.15,1.9,vRockPosition.y);
        // The photographic scan is now only mineral grain, not the main form.
        rockAlbedo *= 1.0 + (grain-.2)*.8*rockReady;
        float rockLuma = mix(grain*.026*rockReady, runnels*.016 + layers*.0008, cliff);
        vec3 peripheral = rockDust * (.48 + broad * .75);
        peripheral *= mix(vec3(1.0), scan * 3.0, .5 * rockReady);
        rockAlbedo = mix(peripheral, rockAlbedo, vMesaMask);
        rockLuma = mix(grain * .018 * rockReady, rockLuma, vMesaMask);
        diffuseColor.rgb *= rockAlbedo;
      `)
      .replace('#include <normal_fragment_maps>', `
        #include <normal_fragment_maps>
        normal = rockSurfaceNormal(-vViewPosition, normal, rockLuma, faceDirection);
      `)
  }
  return material
}
