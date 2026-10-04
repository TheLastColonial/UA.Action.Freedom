Feature: Convoy accommodation API
    Accommodation is booked per crew member at a route point, and every crew member must be
    covered at every overnight stop: by a booking (which several people may share) or by
    arranging their own. A booking that outlives its guest's place on the crew is a warning and
    a Dispatcher task, never a block, and it can be migrated to a replacement.

    Reads are open to every operational role except the Mechanic; writes are Administrator and
    Dispatcher. The Ground Officer is excluded from both.

    These scenarios run against the running containers (the edge on
    http://localhost:8080, Keycloak on http://localhost:8081) and skip themselves
    when that stack is not up.

Background:
    Given the Freedom API exposes "/convoys/1/accommodation"

Scenario: A ground officer is refused the accommodation
    Given I am authenticated as "groundofficer"
    When I GET "/convoys/1/accommodation/coverage"
    Then the response status is 403

Scenario: One shared booking covers the whole crew for the night
    Given I am authenticated as "operator"
    And a convoy with an overnight stop "Lille" crews a driver and a passenger
    When I book "Ibis Lille" at the overnight stop for the driver and the passenger
    Then the response status is 201
    When I GET "/convoys/{id}/accommodation/coverage" on the remembered convoy
    Then the response status is 200
    And the coverage has 0 nights missing

Scenario: A crew member arranging their own night is covered without a booking
    Given I am authenticated as "operator"
    And a convoy with an overnight stop "Lille" crews a driver and a passenger
    When I book "Ibis Lille" at the overnight stop for the passenger
    Then the response status is 201
    When I GET "/convoys/{id}/accommodation/coverage" on the remembered convoy
    Then the coverage has 1 night missing
    When I flag the driver as arranging their own stay at the overnight stop
    Then the response status is 204
    When I GET "/convoys/{id}/accommodation/coverage" on the remembered convoy
    Then the coverage has 0 nights missing

Scenario: A removed crew member's booking becomes a task and is migrated to the replacement
    Given I am authenticated as "operator"
    And a convoy with an overnight stop "Lille" crews a driver and a passenger
    When I book "Ibis Lille" at the overnight stop for the passenger
    Then the response status is 201
    When I GET "/convoys/{id}/tasks" on the remembered convoy
    Then the tasks hold no leftover booking
    When I DELETE "/convoys/{id}/vehicles/{vin}/crew/{passenger}" on the remembered convoy for the passenger
    Then the response status is 204
    When I GET "/convoys/{id}/tasks" on the remembered convoy
    Then the tasks hold a leftover booking
    When I migrate the booking from the passenger to the driver
    Then the response status is 204
    When I GET "/convoys/{id}/tasks" on the remembered convoy
    Then the tasks hold no leftover booking
    When I GET "/convoys/{id}/accommodation/coverage" on the remembered convoy
    Then the coverage has 0 nights missing

Scenario: The route will not drop an overnight stop that has a booking
    Given I am authenticated as "operator"
    And a convoy with an overnight stop "Lille" crews a driver and a passenger
    When I book "Ibis Lille" at the overnight stop for the driver and the passenger
    Then the response status is 201
    When I PUT "/convoys/{id}/route" on the remembered convoy with body:
        """
        { "stops": [] }
        """
    Then the response status is 409
