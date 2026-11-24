/** OSM expects bbox coordinates as west,south,east,north; markers use latitude,longitude. */
export function meetingPointLinks(latitude: number, longitude: number) {
  const bbox = [
    Math.max(-180, longitude - 0.006),
    Math.max(-90, latitude - 0.003),
    Math.min(180, longitude + 0.006),
    Math.min(90, latitude + 0.003),
  ].map((value) => value.toFixed(6)).join(",");
  const params = new URLSearchParams({ bbox, layer: "mapnik", marker: `${latitude},${longitude}` });
  return {
    embed: `https://www.openstreetmap.org/export/embed.html?${params}`,
    full: `https://www.openstreetmap.org/?mlat=${latitude}&mlon=${longitude}#map=16/${latitude}/${longitude}`,
  };
}
