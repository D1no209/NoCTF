import * as THREE from 'three'

/** Object-space rock mapping keeps cliff faces detailed instead of stretching
 * a top-down texture over them. One shared tile and no extra terrain draw calls. */
export function createSpectatorRock(texture: THREE.Texture, tint: THREE.Color, dust: THREE.Color, ready: { value: number }) {
  const material = new THREE.MeshStandardMaterial({
    color: tint, roughness: .96, metalness: 0, vertexColors: true, flatShading: true, side: THREE.DoubleSide,
  })
  material.customProgramCacheKey = () => 'spectator-stratified-rock-v1'
  material.onBeforeCompile = shader => {
    shader.uniforms.rockTexture = { value: texture }
    shader.uniforms.rockReady = ready
    shader.uniforms.rockDust = { value: dust }
    shader.vertexShader = `varying vec3 vRockPosition; varying vec3 vRockNormal;\n${shader.vertexShader}`
      .replace('#include <begin_vertex>', '#include <begin_vertex>\nvRockPosition = position; vRockNormal = normal;')
    shader.fragmentShader = `
      uniform sampler2D rockTexture;
      uniform float rockReady;
      uniform vec3 rockDust;
      varying vec3 vRockPosition;
      varying vec3 vRockNormal;
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
        vec3 rockBase = rockProjection(vRockPosition * .32, rockWeights);
        vec3 rockDetail = rockProjection(vRockPosition * .93 + vec3(4.7, 2.3, 8.1), rockWeights);
        vec3 rockAlbedo = mix(rockBase, rockDetail, .12);
        float rockLuma = dot(rockAlbedo, vec3(.2126, .7152, .0722));
        float rockSlope = 1.0 - abs(normalize(vRockNormal).y);
        float rockLayer = .5 + .5 * sin(vRockPosition.y * 34.0 + rockBase.r * 4.0);
        // Sedimentary seams on cliffs; a little pale mineral dust on flatter ledges.
        rockAlbedo *= mix(1.0, .76 + .24 * rockLayer, smoothstep(.18, .65, rockSlope));
        rockAlbedo *= mix(1.0, .72, smoothstep(.2, .75, rockSlope));
        rockAlbedo *= .48 + .52 * smoothstep(.3, 1.2, vRockPosition.y);
        rockAlbedo = mix(rockAlbedo, rockDust * (.3 + rockLuma), (1.0 - smoothstep(.12, .4, rockSlope)) * .12);
        diffuseColor.rgb *= mix(vec3(1.0), rockAlbedo * 1.65, rockReady);
      `)
      .replace('#include <normal_fragment_maps>', `
        #include <normal_fragment_maps>
        normal = rockSurfaceNormal(-vViewPosition, normal, rockLuma * .13 * rockReady, faceDirection);
      `)
  }
  return material
}
