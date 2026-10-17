namespace PlateBack.Domain.Enums;

// Names travel as strings in JSON and must match the frontend's api.ts exactly.

public enum UserRole
{
    Customer,
    Admin,
}

public enum MemberRole
{
    Owner,
    Staff,
}

public enum OrganizationStatus
{
    Pending,
    Verified,
    Rejected,
    Suspended,
}

public enum BusinessType
{
    Bakery,
    Cafe,
    Restaurant,
    Grocer,
    Other,
}

public enum ListingCategory
{
    Bakery,
    Meals,
    Groceries,
    Beverages,
    Other,
}

public enum ListingStatus
{
    Active,
    SoldOut,
    Expired,
    Cancelled,
}

public enum ReservationStatus
{
    Reserved,
    Collected,
    Cancelled,
    NoShow,
}

public enum NotificationType
{
    ReservationConfirmed,
    ReservationCancelled,
    ReservationCollected,
    ReservationNoShow,
    PickupWindowOpening,
    ListingCancelled,
    NewListingNearby,
    OrganizationVerified,
    OrganizationRejected,
}
