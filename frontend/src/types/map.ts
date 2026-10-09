/** A request to move the map. Used by the locate button, and later by search. */
export interface FlyTarget {
  lat: number;
  lng: number;
  zoom: number;
  /** Changes on every request, so flying to the same place twice still fires. */
  nonce: number;
}

export function flyTarget(lat: number, lng: number, zoom: number): FlyTarget {
  return { lat, lng, zoom, nonce: Date.now() };
}
