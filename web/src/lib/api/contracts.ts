/**
 * Response shapes of the TourDock API.
 *
 * Endpoints that declare their response bodies in the OpenAPI document are aliases of the generated schemas
 * (./schema.d.ts). The rest (auth, agency back office, admin) are still written by hand and checked against real
 * responses; replace them with aliases once the API declares those too.
 */

import type { components } from "./schema";

type Schemas = components["schemas"];

/**
 * The API reads numbers from JSON strings too, so the OpenAPI document types every number as `number | string`.
 * It always writes plain numbers, which is what this maps the generated types to.
 */
export type Wire<T> = number extends T
  ? Exclude<T, string>
  : T extends (infer Item)[]
    ? Wire<Item>[]
    : T extends object
      ? { [Key in keyof T]: Wire<T[Key]> }
      : T;
export type AuthTokens = {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
};

export type CurrentUser = {
  id: string;
  email: string;
  displayName: string;
  roles: string[];
  /** False until the link from the sign-up email is opened; booking and listing an agency need it. */
  emailConfirmed: boolean;
};

export type AuthResponse = {
  user: CurrentUser;
  tokens: AuthTokens;
};

export type AgencyStatus = "PendingApproval" | "Active" | "Suspended";
/** Every member is a manager until member management exists. */
export type AgencyRole = "Manager";

export type MyAgency = {
  id: string;
  name: string;
  contactEmail: string;
  status: AgencyStatus;
  myRole: AgencyRole;
};

export type Money = Wire<Schemas["MoneyDto"]>;

export type BookingMode = Schemas["BookingMode"];

export type TourSort = NonNullable<Schemas["TourSort"]>;

export type TourSummary = Wire<Schemas["TourSummary"]>;
/** A place for the search box; `toursNearby` counts published tours within 25 km. */
export type CitySuggestion = Wire<Schemas["CitySuggestion"]>;
/** A country for the search box, with its places that have the most tours nearby. */
export type CountrySuggestion = Wire<Schemas["CountrySuggestion"]>;
/** Search-box suggestions: matching countries (places nested) and matching places. */
export type PlaceSuggestions = Wire<Schemas["PlaceSuggestions"]>;
/** The soonest departure that fits the search (live seat counts); null when it sold out since the search was cached. */
export type NextDeparture = Wire<Schemas["NextDepartureSummary"]>;

export type PagedResponse<T> = {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
};

/** The travel assistant's answer; `tours` are the tours it cites, `turnsLeft` how many more messages the chat takes. */
export type AssistantReply = Wire<Schemas["AssistantReply"]>;
export type CitedTour = Wire<Schemas["CitedTour"]>;

/**
 * A page of search results. `semantic` is null without a description (q), true when q ordered the results by meaning,
 * false when smart search was unavailable and the usual order was used.
 */
export type TourSearchResponse = PagedResponse<TourSummary> & { semantic: boolean | null };

/** A tour photo in display order. Each upload is served in three sizes (WebP). */
export type TourPhoto = Wire<Schemas["PhotoDto"]>;

export type RefundTier = Wire<Schemas["RefundTierDto"]>;

export type PublicTour = Wire<Schemas["PublicTourResponse"]>;

export type Departure = Wire<Schemas["DepartureResponse"]>;

export type ExchangeRates = Wire<Schemas["ExchangeRatesResponse"]>;

export type TourReviews = Wire<Schemas["TourReviewsResponse"]>;
export type RatingSummary = Wire<Schemas["RatingSummary"]>;
export type PublicReview = Wire<Schemas["PublicReview"]>;
export type ReviewSort = NonNullable<Schemas["ReviewSort"]>;
export type MyReview = Wire<Schemas["MyReview"]>;
export type AgencyReview = Wire<Schemas["AgencyReview"]>;
export type ModerationReview = Wire<Schemas["ModerationReview"]>;
export type ReportReason = Schemas["ReportReason"];
export type AdminActionEntry = Wire<Schemas["AdminActionResponse"]>;
export type FailedRefund = Wire<Schemas["FailedRefundResponse"]>;

export type DepartureWeather = Wire<Schemas["DepartureWeatherResponse"]>;
export type WeatherForecast = Wire<Schemas["WeatherForecastDto"]>;
export type WeatherForecastStatus = Schemas["WeatherForecastStatus"];

export type BookingType = Schemas["BookingType"];
export type BookingStatus = Schemas["BookingStatus"];
export type PaymentStatus = NonNullable<Schemas["PaymentStatus"]>;

export type Booking = Wire<Schemas["BookingResponse"]>;
export type CancellationResult = Wire<Schemas["CancellationResult"]>;

export type Payment = Wire<Schemas["PaymentResponse"]>;

export type AgencyBooking = Wire<Schemas["AgencyBookingResponse"]>;

/** Agency KPIs for a range of local departure dates (`today` ignores the range). */
export type AgencyStats = Wire<Schemas["AgencyStatsResponse"]>;

// ---- Agency back office ----

export type VehicleType = "Boat" | "Car" | "Jeep" | "Bus" | "Other";
export type Vehicle = { id: string; name: string; type: VehicleType; capacity: number };

export type TourStatus = "Draft" | "Published" | "Unpublished";
export type Location = Wire<Schemas["LocationDto"]>;
export type SeasonalPrice = Wire<Schemas["SeasonalPriceDto"]>;

/** Comma-separated day names, e.g. "Monday, Wednesday" (a flags enum on the API). */
export type ScheduleRule = {
  id: string;
  vehicleId: string;
  days: string;
  startTime: string;
  validFrom: string;
  validUntil: string | null;
  isActive: boolean;
};

export type AgencyTour = {
  id: string;
  agencyId: string;
  title: string;
  description: string;
  location: Location;
  durationMinutes: number;
  maxGuests: number;
  bookingMode: BookingMode;
  pricePerPerson: Money | null;
  privatePrice: Money | null;
  cancellationPolicy: RefundTier[];
  status: TourStatus;
  seasonalPrices: SeasonalPrice[];
  scheduleRules: ScheduleRule[];
  photos: TourPhoto[];
};

export type ScheduleRuleCreated = { rule: ScheduleRule; departuresCreated: number; departuresSkippedForConflicts: number };

export type SlotStatus = "Open" | "Blocked" | "Cancelled";
export type Slot = {
  id: string;
  tourId: string;
  vehicleId: string;
  scheduleRuleId: string | null;
  startsAt: string;
  endsAt: string;
  localDate: string;
  capacity: number;
  reservedSeats: number;
  remainingSeats: number;
  bookingMode: BookingMode;
  isPrivatelyBooked: boolean;
  status: SlotStatus;
};

export type AdminAgency = {
  id: string;
  name: string;
  contactEmail: string;
  status: AgencyStatus;
  suspensionReason: string | null;
  createdAt: string;
  managerCount: number;
};
