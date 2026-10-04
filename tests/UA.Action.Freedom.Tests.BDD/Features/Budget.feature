Feature: Convoy budget and costs API
    A convoy has a budget with a line for each cost type, and the costs spent are set beside
    it. Only fuel and other costs are entered; a ferry, hotel or insurance cost is held on its
    booking or policy and read from there, so it is never counted twice. Allocating a budget is
    a step in creating a convoy but is not required to depart: an unset budget, or a line over
    its plan, is advice on the readiness read and nothing more.

    Writes are Administrator and Dispatcher, reads every operational role. The Ground Officer
    is excluded from both.

    These scenarios run against the running containers (the edge on
    http://localhost:8080, Keycloak on http://localhost:8081) and skip themselves
    when that stack is not up.

Background:
    Given the Freedom API exposes "/equipment-items"

Scenario: A ground officer is refused the budget
    Given I am authenticated as "groundofficer"
    When I GET "/convoys/1/budget"
    Then the response status is 403

Scenario: A dispatcher sets a budget, enters fuel over the line and sees the warning
    Given I am authenticated as "operator"
    When I POST "/convoys" with body:
        """
        { "start": "2026-09-01T06:00:00Z", "expectedEnd": "2026-09-05T18:00:00Z" }
        """
    Then the response status is 201
    Given I remember the convoy
    When I GET "/convoys/{id}/readiness" on the remembered convoy
    Then the readiness advises "No budget set"
    When I PUT "/convoys/{id}/budget" on the remembered convoy with body:
        """
        { "lines": [ { "type": "Fuel", "plannedGbp": 1000 }, { "type": "Ferry", "plannedGbp": 600 } ] }
        """
    Then the response status is 204
    When I POST "/convoys/{id}/costs" on the remembered convoy with body:
        """
        { "type": "Fuel", "amountGbp": 1100, "note": "Diesel, Calais" }
        """
    Then the response status is 201
    When I GET "/convoys/{id}/budget/summary" on the remembered convoy
    Then the "Fuel" line of the summary has an actual of 1100
    And the "Fuel" line of the summary is over budget
    When I GET "/convoys/{id}/readiness" on the remembered convoy
    Then the readiness advises "Fuel is over budget"

Scenario: A ferry cost appears as an actual without being entered, and cannot be entered
    Given I am authenticated as "operator"
    When I POST "/convoys" with body:
        """
        { "start": "2026-09-01T06:00:00Z", "expectedEnd": "2026-09-05T18:00:00Z" }
        """
    Then the response status is 201
    Given I remember the convoy
    And no vehicle exists with VIN "WDB9066331S0BDB11"
    And a vehicle exists with VIN "WDB9066331S0BDB11"
    And the vehicle "WDB9066331S0BDB11" has passed its inspection
    When I PUT "/convoys/{id}/vehicles/WDB9066331S0BDB11" on the remembered convoy
    Then the response status is 204
    When I PUT "/convoys/{id}/vehicles/WDB9066331S0BDB11/ferry" on the remembered convoy with body:
        """
        { "operator": "P&O Ferries", "reference": "POF-48213", "sailingAt": "2026-09-02T07:30:00Z", "costGbp": 310 }
        """
    Then the response status is 204
    When I GET "/convoys/{id}/budget/summary" on the remembered convoy
    Then the "Ferry" line of the summary has an actual of 310
    When I POST "/convoys/{id}/costs" on the remembered convoy with body:
        """
        { "type": "Ferry", "amountGbp": 310 }
        """
    Then the response status is 422

Scenario: Equipment bought for a vehicle counts under Other
    Given I am authenticated as "operator"
    When I POST "/convoys" with body:
        """
        { "start": "2026-09-01T06:00:00Z", "expectedEnd": "2026-09-05T18:00:00Z" }
        """
    Then the response status is 201
    Given I remember the convoy
    And no vehicle exists with VIN "WDB9066331S0BDB22"
    And a vehicle exists with VIN "WDB9066331S0BDB22"
    And the vehicle "WDB9066331S0BDB22" has passed its inspection
    When I PUT "/convoys/{id}/vehicles/WDB9066331S0BDB22" on the remembered convoy
    Then the response status is 204
    Given a catalogued equipment item priced at 6.50
    When I PUT "/convoys/{id}/vehicles/WDB9066331S0BDB22/equipment" on the remembered convoy with 2 of the catalogued equipment item
    Then the response status is 204
    When I GET "/convoys/{id}/budget/summary" on the remembered convoy
    Then the "Other" line of the summary has an actual of 13
