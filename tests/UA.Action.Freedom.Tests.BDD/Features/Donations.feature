Feature: Donors and donations API
    The deployed Freedom service records who gave goods. A donor is a split identity with no login: a Dispatcher or
    Loader enters them and their donations, every operational role may read them, and only an Administrator may erase
    one. Erasing a donor deletes their personal details but keeps the donation, its items and its value, which then
    read "Former donor". The donor status report is a high-level account of what was given and shows nothing about
    where it is going. The Ground Officer is excluded from all of it.

    These scenarios run against the running containers (the edge on
    http://localhost:8080, Keycloak on http://localhost:8081) and skip themselves
    when that stack is not up.

Background:
    Given the Freedom API exposes "/donors"

Scenario: Reading donors requires a token
    When I GET "/donors" without a token
    Then the response status is 401

Scenario: A ground officer is refused the donors
    Given I am authenticated as "groundofficer"
    When I GET "/donors"
    Then the response status is 403

Scenario: A dispatcher enters a donor but cannot erase one
    Given I am authenticated as "operator"
    When I POST "/donors" with body:
        """
        { "name": "BDD Donor", "email": "bdd.donor@example.org", "phone": "+447700900456" }
        """
    Then the response status is 201
    And the "Location" header names a new resource
    Given I remember the donor
    When I DELETE "/donors/{donor}"
    Then the response status is 403

Scenario: A donor with no name is rejected
    Given I am authenticated as "operator"
    When I POST "/donors" with body:
        """
        { "name": "" }
        """
    Then the response status is 400
    And the response body names "Name" as invalid

Scenario: A donation is recorded against a donor and reads back with the donor's name
    Given I am authenticated as "operator"
    And a donor exists
    When I POST "/donations" with body:
        """
        { "donorId": "{donor}", "receivedOn": "2026-09-20", "notes": "Two boxes of tins" }
        """
    Then the response status is 201
    Given I remember the donation
    When I GET "/donations/{donation}"
    Then the response status is 200
    And the response body field "donorName" is "BDD Donor"

Scenario: A donation cannot be recorded against a donor who is not on file
    Given I am authenticated as "operator"
    When I POST "/donations" with body:
        """
        { "donorId": "00000000-0000-4000-8000-000000000000", "receivedOn": "2026-09-20" }
        """
    Then the response status is 422

Scenario: Erasing a donor keeps their donation under Former donor
    Given I am authenticated as "operator"
    And a donor exists
    And a donation exists
    And I am authenticated as "admin"
    When I DELETE "/donors/{donor}"
    Then the response status is 204
    When I GET "/donors/{donor}"
    Then the response status is 404
    When I GET "/donations/{donation}"
    Then the response status is 200
    And the response body field "donorName" is "Former donor"

Scenario: The donor report shows what was given and nothing about where it is going
    Given I am authenticated as "operator"
    And a donor exists
    And a donation exists
    And a category exists
    When I POST "/boxes" with body:
        """
        {}
        """
    Then the response status is 201
    Given I remember the box
    When I POST "/boxes/{id}/items" on the remembered box with body:
        """
        { "description": "Tinned soup", "categoryId": {category}, "quantity": 12, "valueGbp": 30.00, "valueSource": "Donor", "donationId": {donation} }
        """
    Then the response status is 200
    When I GET "/donors/{donor}/report"
    Then the response status is 200
    And the response body field "itemCount" is "12"
    And the response body field "totalValueGbp" is "30.00"
    And the response body does not mention "receiver"
    And the response body does not mention "region"
    And the response body does not mention "route"
    And the response body does not mention "address"

Scenario: The donor report is unchanged by erasing the donor
    Given I am authenticated as "operator"
    And a donor exists
    And a donation exists
    And a category exists
    When I POST "/boxes" with body:
        """
        {}
        """
    Then the response status is 201
    Given I remember the box
    When I POST "/boxes/{id}/items" on the remembered box with body:
        """
        { "description": "Tinned soup", "categoryId": {category}, "quantity": 12, "valueGbp": 30.00, "valueSource": "Donor", "donationId": {donation} }
        """
    Then the response status is 200
    Given I am authenticated as "admin"
    When I DELETE "/donors/{donor}"
    Then the response status is 204
    When I GET "/donors/{donor}/report"
    Then the response status is 200
    And the response body field "donorName" is "Former donor"
    And the response body field "itemCount" is "12"
    And the response body field "totalValueGbp" is "30.00"
