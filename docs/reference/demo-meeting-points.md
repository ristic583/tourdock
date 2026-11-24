# Seeded tour meeting points

These are public landmarks for the fictional demo catalog, not confirmed boarding instructions from a real operator. Coordinates identify where the tour starts, not the farthest destination on its itinerary (for example, the Blue Cave trip meets in Split).

| Tours | Meeting point | Latitude, longitude | Reference |
| --- | --- | --- | --- |
| Sunset Cruise; Blue Cave & Five Islands | Riva waterfront, Split | 43.507600, 16.438100 | [OpenStreetMap](https://www.openstreetmap.org/?mlat=43.5076&mlon=16.4381#map=18/43.5076/16.4381) |
| Big Game Fishing Charter | ACI Marina Split, Uvala Baluni | 43.501111, 16.429917 | [Official Split-Dalmatia tourist board marina directory](https://www.dalmatia.hr/tourist-boards-2-2/) |
| Morning Fishing Trip; Pakleni Islands Snorkeling | Riva 21, Hvar harbour | 43.171182, 16.441340 | [City of Hvar: Riva station](https://www.hvar.hr/portal/komunikacija/kvaliteta-zraka/) |
| Konavle Valley Jeep Safari | Pile Gate, Dubrovnik | 42.641730, 18.106850 | [OpenStreetMap Pile Gate](https://www.openstreetmap.org/way/668283215) |
| Bay of Kotor Day Trip | Sea Gate, Kotor old town | 42.424737, 18.769705 | [OpenStreetMap Sea Gate](https://www.openstreetmap.org/node/421915233) |

The data-only migration updates an existing tour only when its demo agency email, title, city, address and previous coordinates all match the original seed. Customized meeting points remain untouched. New databases use the corrected values directly from the seeder. Rolling back application code does not undo these data corrections.
