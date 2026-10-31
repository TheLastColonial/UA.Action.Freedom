Feature: A convoy departs once nothing blocks it
    A convoy departs as a whole, by one action, and only when no blocking requirement is outstanding
    (ADR 0008, O36): its route, a Convoy Leader, and for each vehicle a ferry booking, a registered
    handover Receiver, its boxes and its declarations. A refusal lists every requirement still outstanding.
    There is no override, no waiver and no "depart anyway": the rule is the rule.

    These scenarios run against the running containers (the edge on http://localhost:8080, Keycloak on
    http://localhost:8081) and skip themselves when that stack is not up.

Background:
    Given the Freedom API exposes "/convoys"

Scenario: Departure is refused and lists what is missing, then succeeds once each is met
    Given I am authenticated as "operator"
    And a convoy exists with an insured vehicle on its published truck list
    When I depart the convoy of the insured vehicle
    Then the departure is refused because "route" is outstanding
    And the departure is refused because "leader" is outstanding
    And the departure is refused because "ferry" is outstanding
    And the departure is refused because "handover-receiver" is outstanding
    And the departure is refused because "declaration.gmr" is outstanding
    And the departure is not refused because of "insurance"
    And the departure is not refused because of "driver"

    # Setup completed one step at a time: the list shrinks.
    When I book a ferry for the insured vehicle
    Then the response status is 204
    When I depart the convoy of the insured vehicle
    Then the response status is 409
    And the departure is not refused because of "ferry"
    And the departure is refused because "leader" is outstanding

    When I make the convoy of the insured vehicle ready to depart
    And I depart the convoy of the insured vehicle
    Then the response status is 200
    And the convoy of the insured vehicle has departed

    # Departing is one fact: a second departure is refused.
    When I depart the convoy of the insured vehicle
    Then the response status is 409

Scenario: A convoy cannot depart once a vehicle's insurance is gone
    Given I am authenticated as "operator"
    And a convoy exists with an insured vehicle on its published truck list
    When I make the convoy of the insured vehicle ready to depart
    And I remove the insurance of the insured vehicle
    And I depart the convoy of the insured vehicle
    Then the departure is refused because "insurance" is outstanding

Scenario: There is no route that overrides the requirements
    Given I am authenticated as "admin"
    And a convoy exists with an insured vehicle on its published truck list
    When I POST "/depart-anyway" on the convoy of the insured vehicle
    Then the response status is 404
    When I POST "/override" on the convoy of the insured vehicle
    Then the response status is 404

Scenario: A ground officer cannot depart a convoy
    Given I am authenticated as "operator"
    And a convoy exists with an insured vehicle on its published truck list
    Given I am authenticated as "groundofficer"
    When I depart the convoy of the insured vehicle
    Then the response status is 403
