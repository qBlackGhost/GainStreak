namespace GymApp.Contracts;

/// <summary>How a found user relates to you, for the Add / Requested / Accept / Friends button. Sent as a camelCase string.</summary>
public enum FriendRelation { None, Friend, RequestSent, RequestReceived }

public sealed record FoundUserResponse(int UserId, string Username, FriendRelation Relation);

public sealed record FriendResponse(int UserId, string Username, DateTime Since);

public sealed record FriendRequestResponse(int UserId, string Username, DateTime SentAt);

/// <summary>Pending requests, newest first.</summary>
public sealed record FriendRequestsResponse(IReadOnlyList<FriendRequestResponse> Incoming, IReadOnlyList<FriendRequestResponse> Outgoing);

public sealed record CountResponse(int Count);

/// <summary><see cref="BecameFriends"/> is true when the other user had already sent you a request, so sending accepted it.</summary>
public sealed record SendRequestResponse(bool BecameFriends);

/// <summary>Your invite code and the link (and QR code) that carries it: <c>https://&lt;host&gt;/add/&lt;code&gt;</c>. Phase 21.</summary>
public sealed record FriendInviteResponse(string Code, string Url);

/// <summary>Whose invite code this is and how they relate to you, for "alex wants to be your friend". Phase 21.</summary>
public sealed record InviteLookupResponse(int UserId, string Username, FriendRelation Relation);
