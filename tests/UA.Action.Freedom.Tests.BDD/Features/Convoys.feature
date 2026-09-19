Feature: Convoys API
    The deployed Freedom service exposes convoys at /convoys, together with their route and
    their truck list. Reads are open to every operational role; planning a convoy is
    Administrator and Dispatcher. The Ground Officer is excluded from both.

    Publishing the truck list is the gate in docs/process.puml — Truck List Created, then
    Truck List Published, then Manifest Proposed. Manifests are proposed against the
    published set of vehicles, so publication happens once and closes that set.

    A vehicle is itself part of the aid, handed over in Ukraine, so only one a Mechanic has
    passed at inspection may join a truck list. Crewing a vehicle on the list is the
    Dispatcher's alone.

    These scenarios run against the running containers (the edge on
    http://localhost:8080, Keycloak on http://localhost:8081) and skip themselves
    when that stack is not up.

Background:
    Given the Freedom API exposes "/convoys"

Scenario: Listing convoys requires a token
    When I GET "/convoys" without a token
    Then the response status is 401

Scenario: A ground officer is refused convoy reads
    Given I am authenticated as "groundofficer"
    When I GET "/convoys"
    Then the response status is 403

Scenario: A dispatcher plans a convoy
    Given I am authenticated as "operator"
    When I POST "/convoys" with body:
        """
        { "start": "2026-09-01T06:00:00Z", "expectedEnd": "2026-09-05T18:00:00Z" }
        """
    Then the response status is 201
    And the "Location" header names a new resource

Scenario: A convoy that arrives before it departs is rejected
    Given I am authenticated as "operator"
    When I POST "/convoys" with body:
        """
        { "start": "2026-09-05T18:00:00Z", "expectedEnd": "2026-09-01T06:00:00Z" }
        """
    Then the response status is 400

Scenario: A convoy's route is stored in the order it was sent
    Given I am authenticated as "operator"
    When I POST "/convoys" with body:
        """
        { "start": "2026-09-01T06:00:00Z", "expectedEnd": "2026-09-05T18:00:00Z" }
        """
    Then the response status is 201
    When I PUT "/convoys/{id}/route" with body:
        """
        { "stops": [
            { "house": "Unit 4", "street": "Cross Road", "city": "Coventry", "country": "United Kingdom", "postcode": "CV1 2AB" },
            { "street": "Trasa Katowicka", "city": "Warszawa", "country": "Poland", "postcode": "80-180" }
        ] }
        """
    Then the response status is 204
    When I GET "/convoys/{id}/route"
    Then the response status is 200
    And the response body lists a route of 2 stops
    And route stop 1 is in "Coventry"
    And route stop 2 is in "Warszawa"

Scenario: A convoy with no route planned yet has an empty route, not a missing one
    Given I am authenticated as "operator"
    When I POST "/convoys" with body:
        """
        { "start": "2026-09-01T06:00:00Z", "expectedEnd": "2026-09-05T18:00:00Z" }
        """
    Then the response status is 201
    When I GET "/convoys/{id}/route"
    Then the response status is 200
    And the response body lists a route of 0 stops

Scenario: A dispatcher builds a truck list, publishes it, and can no longer change it
    Given I am authenticated as "operator"
    When I POST "/convoys" with body:
        """
        { "start": "2026-09-01T06:00:00Z", "expectedEnd": "2026-09-05T18:00:00Z" }
        """
    Then the response status is 201
    Given I remember the convoy
    And no vehicle exists with VIN "WDB9066331S0BDC77"
    And a vehicle exists with VIN "WDB9066331S0BDC77"
    And the vehicle "WDB9066331S0BDC77" has passed its inspection
    When I PUT "/convoys/{id}/vehicles/WDB9066331S0BDC77" on the remembered convoy
    Then the response status is 204
    When I GET "/convoys/{id}/vehicles" on the remembered convoy
    Then the response status is 200
    And the response body lists a vehicle with VIN "WDB9066331S0BDC77"
    When I POST "/convoys/{id}/publish-truck-list" on the remembered convoy
    Then the response status is 204
    When I POST "/convoys/{id}/publish-truck-list" on the remembered convoy
    Then the response status is 409
    When I DELETE "/convoys/{id}/vehicles/WDB9066331S0BDC77" on the remembered convoy
    Then the response status is 409
    When I GET "/convoys/{id}" on the remembered convoy
    Then the response body field "truckListPublished" is "True"

Scenario: A vehicle cannot join a truck list that has already been published
    Given I am authenticated as "operator"
    When I POST "/convoys" with body:
        """
        { "start": "2026-09-01T06:00:00Z", "expectedEnd": "2026-09-05T18:00:00Z" }
        """
    Then the response status is 201
    Given I remember the convoy
    And no vehicle exists with VIN "WDB9066331S0BDC88"
    And a vehicle exists with VIN "WDB9066331S0BDC88"
    And the vehicle "WDB9066331S0BDC88" has passed its inspection
    When I POST "/convoys/{id}/publish-truck-list" on the remembered convoy
    Then the response status is 204
    When I PUT "/convoys/{id}/vehicles/WDB9066331S0BDC88" on the remembered convoy
    Then the response status is 409
    When I GET "/convoys/{id}/vehicles" on the remembered convoy
    Then the response body lists no vehicles

Scenario: A vehicle that has not passed its inspection cannot join a truck list
    Given I am authenticated as "operator"
    When I POST "/convoys" with body:
        """
        { "start": "2026-09-01T06:00:00Z", "expectedEnd": "2026-09-05T18:00:00Z" }
        """
    Then the response status is 201
    Given I remember the convoy
    And no vehicle exists with VIN "WDB9066331S0BDC99"
    And a vehicle exists with VIN "WDB9066331S0BDC99"
    When I PUT "/convoys/{id}/vehicles/WDB9066331S0BDC99" on the remembered convoy
    Then the response status is 409
    When I GET "/convoys/{id}/vehicles" on the remembered convoy
    Then the response body lists no vehicles

Scenario: A dispatcher crews a vehicle on the truck list and stands the driver down again
    Given I am authenticated as "operator"
    When I POST "/convoys" with body:
        """
        { "start": "2026-09-01T06:00:00Z", "expectedEnd": "2026-09-05T18:00:00Z" }
        """
    Then the response status is 201
    Given I remember the convoy
    And no vehicle exists with VIN "WDB9066331S0BDC66"
    And a vehicle exists with VIN "WDB9066331S0BDC66"
    And the vehicle "WDB9066331S0BDC66" has passed its inspection
    And a driver exists
    When I PUT "/convoys/{id}/vehicles/WDB9066331S0BDC66" on the remembered convoy
    Then the response status is 204
    When I PUT "/convoys/{id}/vehicles/WDB9066331S0BDC66/drivers/{driver}" on the remembered convoy for the driver
    Then the response status is 204
    When I PUT "/convoys/{id}/vehicles/WDB9066331S0BDC66/drivers/{driver}" on the remembered convoy for the driver
    Then the response status is 409
    When I GET "/convoys/{id}/vehicles/WDB9066331S0BDC66/drivers" on the remembered convoy
    Then the response status is 200
    And the response body lists the driver
    When I DELETE "/convoys/{id}/vehicles/WDB9066331S0BDC66/drivers/{driver}" on the remembered convoy for the driver
    Then the response status is 204

Scenario: A volunteer who does not drive rides as a passenger, and takes only one seat per convoy
    Given I am authenticated as "operator"
    When I POST "/convoys" with body:
        """
        { "start": "2026-09-01T06:00:00Z", "expectedEnd": "2026-09-05T18:00:00Z" }
        """
    Then the response status is 201
    Given I remember the convoy
    And no vehicle exists with VIN "WDB9066331S0BDC44"
    And a vehicle exists with VIN "WDB9066331S0BDC44"
    And the vehicle "WDB9066331S0BDC44" has passed its inspection
    And no vehicle exists with VIN "WDB9066331S0BDC33"
    And a vehicle exists with VIN "WDB9066331S0BDC33"
    And the vehicle "WDB9066331S0BDC33" has passed its inspection
    And a volunteer who does not drive exists
    When I PUT "/convoys/{id}/vehicles/WDB9066331S0BDC44" on the remembered convoy
    And I PUT "/convoys/{id}/vehicles/WDB9066331S0BDC33" on the remembered convoy
    And I PUT "/convoys/{id}/vehicles/WDB9066331S0BDC44/drivers/{passenger}" on the remembered convoy for the passenger with body:
        """
        { "role": "Passenger" }
        """
    Then the response status is 204
    When I GET "/convoys/{id}/vehicles/WDB9066331S0BDC44/drivers" on the remembered convoy
    Then the crew lists the passenger as a "Passenger"
    When I PUT "/convoys/{id}/vehicles/WDB9066331S0BDC33/drivers/{passenger}" on the remembered convoy for the passenger with body:
        """
        { "role": "Passenger" }
        """
    Then the response status is 409

Scenario: An administrator may not crew a vehicle
    Given I am authenticated as "admin"
    When I POST "/convoys" with body:
        """
        { "start": "2026-09-01T06:00:00Z", "expectedEnd": "2026-09-05T18:00:00Z" }
        """
    Then the response status is 201
    Given I remember the convoy
    And no vehicle exists with VIN "WDB9066331S0BDC55"
    And a vehicle exists with VIN "WDB9066331S0BDC55"
    And the vehicle "WDB9066331S0BDC55" has passed its inspection
    And a driver exists
    When I PUT "/convoys/{id}/vehicles/WDB9066331S0BDC55" on the remembered convoy
    Then the response status is 204
    When I PUT "/convoys/{id}/vehicles/WDB9066331S0BDC55/drivers/{driver}" on the remembered convoy for the driver
    Then the response status is 403

Scenario: Putting an unknown vehicle on a truck list is a 404
    Given I am authenticated as "operator"
    When I POST "/convoys" with body:
        """
        { "start": "2026-09-01T06:00:00Z", "expectedEnd": "2026-09-05T18:00:00Z" }
        """
    Then the response status is 201
    When I PUT "/convoys/{id}/vehicles/NOSUCHVIN0000BDD1"
    Then the response status is 404

Scenario: Fetching an unknown convoy is a 404
    Given I am authenticated as "operator"
    When I GET "/convoys/99999999"
    Then the response status is 404

Scenario: Publishing the truck list of an unknown convoy is a 404
    Given I am authenticated as "operator"
    When I POST "/convoys/99999999/publish-truck-list"
    Then the response status is 404
