Feature: Scoped permissions
    A Convoy Leader acts on their own convoy only, and only while they lead it. A Loader sees only the
    locations an Administrator has assigned them. Neither is a role the identity provider issues for a
    particular convoy or location: the API decides from the assignment on every request (ADR 0010).

    The "leader" seed login carries no application role at all. It becomes a Convoy Leader when a
    Dispatcher nominates the volunteer it is linked to. The "loader" seed login is a Loader and
    nothing else, so unlike "operator" (which also holds Dispatcher) it is narrowed by scope.

    These scenarios run against the running containers (the edge on http://localhost:8080,
    Keycloak on http://localhost:8081) and skip themselves when that stack is not up.

Background:
    Given the Freedom API exposes "/locations"

Scenario: A leader reads their own convoy and is refused another, and loses access when reassigned
    Given I am authenticated as "operator"
    When I POST "/convoys" with body:
        """
        { "start": "2026-09-01T06:00:00Z", "expectedEnd": "2026-09-05T18:00:00Z" }
        """
    Then the response status is 201
    Given I remember the convoy
    And no vehicle exists with VIN "WDB9066331S0BSC01"
    And a vehicle exists with VIN "WDB9066331S0BSC01"
    And the vehicle "WDB9066331S0BSC01" has passed its inspection
    And the "leader" login's volunteer is the driver
    When I PUT "/convoys/{id}/vehicles/WDB9066331S0BSC01" on the remembered convoy
    Then the response status is 204
    When I PUT "/convoys/{id}/vehicles/WDB9066331S0BSC01/crew/{driver}" on the remembered convoy for the driver
    Then the response status is 204
    When I nominate the driver as the leader of the remembered convoy
    Then the response status is 200
    When I POST "/convoys" with body:
        """
        { "start": "2026-09-08T06:00:00Z", "expectedEnd": "2026-09-12T18:00:00Z" }
        """
    Then the response status is 201
    Given I am authenticated as "leader"
    When I GET "/convoys/{id}/vehicles" on the remembered convoy
    Then the response status is 200
    When I GET "/convoys/{id}"
    Then the response status is 403
    When I GET "/convoys"
    Then the response status is 403
    Given I am authenticated as "operator"
    And a driver exists
    When I PUT "/convoys/{id}/vehicles/WDB9066331S0BSC01/crew/{driver}" on the remembered convoy for the driver
    Then the response status is 204
    When I nominate the driver as the leader of the remembered convoy
    Then the response status is 200
    Given I am authenticated as "leader"
    When I GET "/convoys/{id}/vehicles" on the remembered convoy
    Then the response status is 403

Scenario: A Loader sees only the location they manage and the boxes at it
    Given the "loader" login manages a location
    And a location the loader does not manage exists
    And a box exists at the location the loader manages
    And a box exists at the location the loader does not manage
    And I am authenticated as "loader"
    When I GET "/boxes/{pinned}" for the pinned "myBox"
    Then the response status is 200
    When I GET "/boxes/{pinned}" for the pinned "theirBox"
    Then the response status is 403
    When I POST "/boxes/{pinned}/qr-code" for the pinned "theirBox"
    Then the response status is 403
    When I GET "/boxes?pageSize=200"
    Then the response lists the pinned "myBox"
    And the response does not list the pinned "theirBox"
    When I GET "/locations"
    Then the response lists the pinned "mine"
    And the response does not list the pinned "theirs"
    When I GET "/locations/{pinned}" for the pinned "theirs"
    Then the response status is 403

Scenario: An operator who is also a Loader is not narrowed, because roles union
    Given the "loader" login manages a location
    And a location the loader does not manage exists
    And I am authenticated as "operator"
    When I GET "/locations"
    Then the response lists the pinned "mine"
    And the response lists the pinned "theirs"
